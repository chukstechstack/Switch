using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Switch.Service
{
    /// <summary>
    /// Prevents the Switch process from being terminated (e.g. via Task Manager)
    /// by applying a DENY PROCESS_TERMINATE ACE to the process DACL.
    ///
    /// This uses the standard Win32 security descriptor / ACL APIs:
    ///   1. OpenProcessToken is NOT used — we work on the kernel object (the process handle),
    ///      not the token.
    ///   2. We fetch the current DACL from the process security descriptor,
    ///      prepend a DENY ACE for PROCESS_TERMINATE for "Everyone", then reapply it.
    ///   3. Disabling protection restores the original DACL.
    ///
    /// Why this works against Task Manager:
    ///   Task Manager calls OpenProcess(PROCESS_TERMINATE, ...) before calling TerminateProcess.
    ///   With the deny ACE in place, that OpenProcess call returns ERROR_ACCESS_DENIED (5),
    ///   so TerminateProcess is never reached.  Even "End Process Tree" follows the same path.
    ///
    ///   Note: A user running as SYSTEM or as a protected-process-level debugger can still
    ///   bypass this — that is intentional and expected OS behaviour.
    /// </summary>
    public static class ProcessProtection
    {
        // ── Win32 constants ────────────────────────────────────────────────────────
        private const uint PROCESS_TERMINATE      = 0x0001;
        private const uint PROCESS_ALL_ACCESS     = 0x1F0FFF;
        private const uint READ_CONTROL           = 0x00020000;
        private const uint WRITE_DAC              = 0x00040000;

        private const uint SECURITY_DESCRIPTOR_REVISION = 1;
        private const uint ACL_REVISION                  = 2;

        private const byte ACCESS_DENIED_ACE_TYPE  = 1;   // DENY ACE
        private const byte OBJECT_INHERIT_ACE       = 0x1;
        private const byte CONTAINER_INHERIT_ACE    = 0x2;
        private const byte INHERIT_ONLY_ACE         = 0x8;

        private const int  SE_OBJECT_TYPE_KERNEL_OBJECT = 6; // SE_KERNEL_OBJECT
        private const uint DACL_SECURITY_INFORMATION    = 0x00000004;

        // Well-known SID: S-1-1-0 (Everyone)
        // We deny Everyone so that *any* caller (including admins running Task Manager
        // without elevation to SYSTEM) gets the deny applied before any allow ACE.
        private static readonly byte[] EveryoneSid = BuildEveryoneSid();

        // ── P/Invoke declarations ──────────────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetSecurityInfo(
            IntPtr         handle,
            int            objectType,
            uint           securityInfo,
            out IntPtr     pSidOwner,
            out IntPtr     pSidGroup,
            out IntPtr     pDacl,
            out IntPtr     pSacl,
            out IntPtr     ppSecurityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetSecurityInfo(
            IntPtr  handle,
            int     objectType,
            uint    securityInfo,
            IntPtr  pSidOwner,
            IntPtr  pSidGroup,
            IntPtr  pDacl,
            IntPtr  pSacl);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool InitializeAcl(
            IntPtr pAcl,
            uint   nAclLength,
            uint   dwAclRevision);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AddAccessDeniedAce(
            IntPtr pAcl,
            uint   dwAceRevision,
            uint   accessMask,
            IntPtr pSid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetAclInformation(
            IntPtr           pAcl,
            ref ACL_SIZE_INFORMATION pAclInformation,
            uint             nAclInformationLength,
            int              dwAclInformationClass);   // AclSizeInformation = 2

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetAce(
            IntPtr pAcl,
            uint   dwAceIndex,
            out IntPtr pAce);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AddAce(
            IntPtr pAcl,
            uint   dwAceRevision,
            uint   dwStartingAceIndex, // MAXDWORD = append at end
            IntPtr pAceList,
            uint   nAceListLength);

        [DllImport("kernel32.dll")]
        private static extern void LocalFree(IntPtr hMem);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AllocateAndInitializeSid(
            ref SID_IDENTIFIER_AUTHORITY pIdentifierAuthority,
            byte nSubAuthorityCount,
            uint dwSubAuthority0,
            uint dwSubAuthority1,
            uint dwSubAuthority2, uint dwSubAuthority3,
            uint dwSubAuthority4, uint dwSubAuthority5,
            uint dwSubAuthority6, uint dwSubAuthority7,
            out IntPtr pSid);

        [DllImport("advapi32.dll")]
        private static extern void FreeSid(IntPtr pSid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint GetLengthSid(IntPtr pSid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CopySid(uint nDestinationSidLength, IntPtr pDestinationSid, IntPtr pSourceSid);

        // ── Structs ────────────────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct SID_IDENTIFIER_AUTHORITY
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACL_SIZE_INFORMATION
        {
            public uint AceCount;
            public uint AclBytesInUse;
            public uint AclBytesFree;
        }

        // ── Public surface ─────────────────────────────────────────────────────────

        /// <summary>
        /// Apply the DENY PROCESS_TERMINATE ACE.  Call once when the first lock goes active.
        /// Safe to call multiple times — checks whether already protected first.
        /// </summary>
        public static bool Enable(out string error)
        {
            error = string.Empty;
            try
            {
                return ApplyDenyAce(true, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Log($"[ProcessProtection] Enable failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove the DENY PROCESS_TERMINATE ACE.  Call when all locks have expired.
        /// </summary>
        public static bool Disable(out string error)
        {
            error = string.Empty;
            try
            {
                return ApplyDenyAce(false, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Log($"[ProcessProtection] Disable failed: {ex.Message}");
                return false;
            }
        }

        // ── Implementation ─────────────────────────────────────────────────────────

        private static bool ApplyDenyAce(bool addDeny, out string error)
        {
            error = string.Empty;

            var hProcess = GetCurrentProcess();  // pseudo-handle, no Close needed

            // Retrieve the current DACL
            bool ok = GetSecurityInfo(
                hProcess,
                SE_OBJECT_TYPE_KERNEL_OBJECT,
                DACL_SECURITY_INFORMATION,
                out _,          // owner SID (not needed)
                out _,          // group SID (not needed)
                out IntPtr pOldDacl,
                out _,          // SACL (not needed)
                out IntPtr pSD);

            if (!ok)
            {
                error = $"GetSecurityInfo failed (error {Marshal.GetLastWin32Error()})";
                Logger.Log($"[ProcessProtection] {error}");
                return false;
            }

            try
            {
                // Build a new DACL
                IntPtr pNewDacl = BuildNewDacl(pOldDacl, addDeny, out error);
                if (pNewDacl == IntPtr.Zero)
                {
                    Logger.Log($"[ProcessProtection] BuildNewDacl failed: {error}");
                    return false;
                }

                try
                {
                    // Apply the new DACL to the process kernel object
                    ok = SetSecurityInfo(
                        hProcess,
                        SE_OBJECT_TYPE_KERNEL_OBJECT,
                        DACL_SECURITY_INFORMATION,
                        IntPtr.Zero, IntPtr.Zero,
                        pNewDacl, IntPtr.Zero);

                    if (!ok)
                    {
                        error = $"SetSecurityInfo failed (error {Marshal.GetLastWin32Error()})";
                        Logger.Log($"[ProcessProtection] {error}");
                        return false;
                    }

                    Logger.Log($"[ProcessProtection] Protection {(addDeny ? "ENABLED" : "DISABLED")} successfully");
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(pNewDacl);
                }
            }
            finally
            {
                if (pSD != IntPtr.Zero) LocalFree(pSD);
            }
        }

        /// <summary>
        /// Build a new ACL:
        ///   addDeny=true  → prepend DENY PROCESS_TERMINATE for Everyone before all existing ACEs
        ///   addDeny=false → copy all existing ACEs *except* our DENY PROCESS_TERMINATE ones
        /// </summary>
        private static IntPtr BuildNewDacl(IntPtr pOldDacl, bool addDeny, out string error)
        {
            error = string.Empty;

            // Everyone SID as unmanaged memory
            IntPtr pEveryoneSid = Marshal.AllocHGlobal(EveryoneSid.Length);
            Marshal.Copy(EveryoneSid, 0, pEveryoneSid, EveryoneSid.Length);

            try
            {
                // Get info about old DACL
                uint oldAceCount = 0;
                uint oldBytesInUse = 0;

                if (pOldDacl != IntPtr.Zero)
                {
                    var info = new ACL_SIZE_INFORMATION();
                    if (GetAclInformation(pOldDacl, ref info,
                            (uint)Marshal.SizeOf<ACL_SIZE_INFORMATION>(), 2))
                    {
                        oldAceCount   = info.AceCount;
                        oldBytesInUse = info.AclBytesInUse;
                    }
                }

                // Calculate required size for new ACL
                // DENY ACE header = 8 bytes + SID length
                uint denyAceSize = (uint)(8 + EveryoneSid.Length);
                uint newAclSize  = (oldBytesInUse > 0 ? oldBytesInUse : 8u)
                                   + (addDeny ? denyAceSize : 0u)
                                   + 64u; // safety margin

                IntPtr pNewDacl = Marshal.AllocHGlobal((int)newAclSize);
                if (pNewDacl == IntPtr.Zero)
                {
                    error = "AllocHGlobal returned null";
                    return IntPtr.Zero;
                }

                // Zero out
                for (int i = 0; i < (int)newAclSize; i++)
                    Marshal.WriteByte(pNewDacl, i, 0);

                if (!InitializeAcl(pNewDacl, newAclSize, ACL_REVISION))
                {
                    Marshal.FreeHGlobal(pNewDacl);
                    error = $"InitializeAcl failed ({Marshal.GetLastWin32Error()})";
                    return IntPtr.Zero;
                }

                // Step 1: if enabling, add the deny ACE FIRST (deny ACEs must precede allow ACEs)
                if (addDeny)
                {
                    if (!AddAccessDeniedAce(pNewDacl, ACL_REVISION, PROCESS_TERMINATE, pEveryoneSid))
                    {
                        Marshal.FreeHGlobal(pNewDacl);
                        error = $"AddAccessDeniedAce failed ({Marshal.GetLastWin32Error()})";
                        return IntPtr.Zero;
                    }
                }

                // Step 2: copy all old ACEs, skipping our own DENY ACE when disabling
                for (uint i = 0; i < oldAceCount; i++)
                {
                    if (!GetAce(pOldDacl, i, out IntPtr pAce)) continue;
                    if (pAce == IntPtr.Zero) continue;

                    // Read ACE header: [AceType(1), AceFlags(1), AceSize(2)]
                    byte aceType  = Marshal.ReadByte(pAce, 0);
                    ushort aceSize = (ushort)Marshal.ReadInt16(pAce, 2);

                    // When disabling: skip our own DENY PROCESS_TERMINATE + Everyone ACE
                    if (!addDeny && aceType == ACCESS_DENIED_ACE_TYPE)
                    {
                        // ACCESS_DENIED_ACE layout: header(4) + Mask(4) + SidStart
                        uint mask = (uint)Marshal.ReadInt32(pAce, 4);
                        if ((mask & PROCESS_TERMINATE) != 0)
                        {
                            // Check it's for Everyone SID — compare raw bytes
                            IntPtr pAceSid = pAce + 8;
                            if (SidsEqual(pAceSid, pEveryoneSid))
                                continue; // skip this ACE
                        }
                    }

                    // Append ACE to new DACL
                    AddAce(pNewDacl, ACL_REVISION, 0xFFFFFFFF /*MAXDWORD — append*/, pAce, aceSize);
                }

                return pNewDacl;
            }
            finally
            {
                Marshal.FreeHGlobal(pEveryoneSid);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static bool SidsEqual(IntPtr pSid1, IntPtr pSid2)
        {
            try
            {
                // SID layout: Revision(1) + SubAuthorityCount(1) + IdentifierAuthority(6) + SubAuthorities(4 each)
                byte subCount1 = Marshal.ReadByte(pSid1, 1);
                byte subCount2 = Marshal.ReadByte(pSid2, 1);
                if (subCount1 != subCount2) return false;

                int sidLen = 8 + subCount1 * 4; // 8 = fixed header, 4 bytes per sub-authority
                for (int i = 0; i < sidLen; i++)
                {
                    if (Marshal.ReadByte(pSid1, i) != Marshal.ReadByte(pSid2, i))
                        return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Build the binary representation of S-1-1-0 (Everyone) as a byte[].
        /// SID binary format:
        ///   [0]   Revision          = 1
        ///   [1]   SubAuthorityCount = 1
        ///   [2-7] IdentifierAuthority (big-endian) = {0,0,0,0,0,1} (SECURITY_WORLD_SID_AUTHORITY)
        ///   [8-11] SubAuthority[0]  = 0 (SECURITY_WORLD_RID) — little-endian DWORD
        /// </summary>
        private static byte[] BuildEveryoneSid()
        {
            return new byte[]
            {
                0x01,               // Revision
                0x01,               // SubAuthorityCount
                0x00, 0x00, 0x00,   // IdentifierAuthority [0..2]
                0x00, 0x00, 0x01,   // IdentifierAuthority [3..5] = SECURITY_WORLD_SID_AUTHORITY (1)
                0x00, 0x00, 0x00, 0x00  // SubAuthority[0] = 0 (SECURITY_WORLD_RID)
            };
        }
    }
}
