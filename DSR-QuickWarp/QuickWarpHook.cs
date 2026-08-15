using System;
using DSR_Gadget;
using PropertyHook;

namespace DSR_QuickWarp
{
    internal sealed class QuickWarpHook : PHook
    {
        private readonly PHPointer _worldChrBase;
        private readonly PHPointer _playerIns;
        private readonly PHPointer _chrClassWarp;
        private readonly PHPointer _gameDataManBase;
        private readonly PHPointer _bonfireWarpAddress;

        internal QuickWarpHook()
            : base(500, 1000, process => process.MainWindowTitle == "DARK SOULS™: REMASTERED")
        {
            _worldChrBase = RegisterRelativeAOB(
                DSROffsets.WorldChrManImpBaseAOB,
                3,
                7,
                DSROffsets.WorldChrManImpBaseOffset1);

            _playerIns = CreateChildPointer(
                _worldChrBase,
                (int)DSROffsets.WorldChrManImp.PlayerIns);

            _chrClassWarp = RegisterRelativeAOB(
                DSROffsets.ChrClassWarpAOB,
                3,
                7,
                DSROffsets.ChrClassWarpOffset1);

            _gameDataManBase = RegisterRelativeAOB(
                DSROffsets.GameDataManAOB,
                3,
                7,
                DSROffsets.GameDataManOffset1);

            _bonfireWarpAddress = RegisterAbsoluteAOB(DSROffsets.BonfireWarpAOB);
        }

        internal bool Ready
        {
            get
            {
                return Hooked && AOBScanSucceeded && _playerIns.Resolve() != IntPtr.Zero;
            }
        }

        internal bool MapWarpReady
        {
            get
            {
                return Ready
                    && _chrClassWarp.Resolve() != IntPtr.Zero
                    && _gameDataManBase.Resolve() != IntPtr.Zero
                    && _bonfireWarpAddress.Resolve() != IntPtr.Zero;
            }
        }

        internal QuickWarpPlayer GetPlayer()
        {
            return new QuickWarpPlayer(_playerIns, this);
        }

        internal bool TryBonfireWarp(int targetBonfireId, out string error)
        {
            error = null;
            if (!MapWarpReady)
            {
                error = "Game map-warp function is not ready.";
                return false;
            }

            int originalBonfire = _chrClassWarp.ReadInt32((int)DSROffsets.ChrClassWarp.LastBonfire);
            try
            {
                _chrClassWarp.WriteInt32((int)DSROffsets.ChrClassWarp.LastBonfire, targetBonfireId);

                // Same remote-call stub used by DSR Gadget's DSRHook.BonfireWarp().
                // rcx = *GameDataManBase, edx = 1, then call the game's BonfireWarp function.
                byte[] asm =
                {
                    0x48, 0xB9,
                    0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
                    0x48, 0x8B, 0x09,
                    0xBA, 0x01, 0x00, 0x00, 0x00,
                    0x48, 0x83, 0xEC, 0x38,
                    0x49, 0xBE,
                    0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
                    0x41, 0xFF, 0xD6,
                    0x48, 0x83, 0xC4, 0x38,
                    0xC3
                };

                byte[] baseBytes = BitConverter.GetBytes(_gameDataManBase.Resolve().ToInt64());
                Array.Copy(baseBytes, 0, asm, 0x2, 8);
                byte[] functionBytes = BitConverter.GetBytes(_bonfireWarpAddress.Resolve().ToInt64());
                Array.Copy(functionBytes, 0, asm, 0x18, 8);

                Execute(asm);
                return true;
            }
            catch (Exception ex)
            {
                error = "Map warp failed: " + ex.Message;
                return false;
            }
            finally
            {
                // The game function has already consumed the target by the time Execute returns.
                // Restore the player's real respawn bonfire immediately so QuickWarp does not
                // permanently alter normal death/respawn behavior.
                try
                {
                    if (_chrClassWarp.Resolve() != IntPtr.Zero)
                        _chrClassWarp.WriteInt32((int)DSROffsets.ChrClassWarp.LastBonfire, originalBonfire);
                }
                catch
                {
                }
            }
        }
    }
}
