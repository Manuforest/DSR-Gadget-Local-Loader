using System;
using DSR_Gadget;
using PropertyHook;

namespace DSR_QuickWarp
{
    internal sealed class QuickWarpHook : PHook
    {
        private readonly PHPointer _worldChrBase;
        private readonly PHPointer _playerIns;

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
        }

        internal bool Ready
        {
            get
            {
                return Hooked && AOBScanSucceeded && _playerIns.Resolve() != IntPtr.Zero;
            }
        }

        internal QuickWarpPlayer GetPlayer()
        {
            return new QuickWarpPlayer(_playerIns, this);
        }
    }
}
