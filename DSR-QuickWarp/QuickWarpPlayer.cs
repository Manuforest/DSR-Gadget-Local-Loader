using System;
using DSR_Gadget;
using PropertyHook;

namespace DSR_QuickWarp
{
    internal sealed class QuickWarpPlayer
    {
        private readonly PHPointer _playerIns;
        private readonly PHPointer _playerCtrl;
        private readonly PHPointer _chrPosData;

        internal QuickWarpPlayer(PHPointer playerIns, QuickWarpHook hook)
        {
            _playerIns = playerIns;
            _playerCtrl = hook.CreateChildPointer(
                _playerIns,
                (int)DSROffsets.PlayerIns.PlayerCtrl);
            _chrPosData = hook.CreateChildPointer(
                _playerCtrl,
                (int)DSROffsets.PlayerCtrl.ChrPosData);
        }

        internal bool Ready
        {
            get
            {
                return _playerIns.Resolve() != IntPtr.Zero
                    && _playerCtrl.Resolve() != IntPtr.Zero
                    && _chrPosData.Resolve() != IntPtr.Zero;
            }
        }

        internal int AreaId
        {
            get { return _playerIns.ReadInt32((int)DSROffsets.PlayerIns.AreaID); }
        }

        internal int MpAreaId
        {
            get { return _playerIns.ReadInt32((int)DSROffsets.PlayerIns.MPAreaID); }
        }

        internal int MapGroup
        {
            get { return WarpMap.GetGroup(AreaId, MpAreaId); }
        }

        internal Position GetPosition()
        {
            return new Position(
                _chrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosX),
                _chrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosY),
                _chrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosZ),
                _chrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosAngle));
        }

        internal void Warp(Position position)
        {
            _playerCtrl.WriteSingle((int)DSROffsets.PlayerCtrl.WarpX, position.X);
            _playerCtrl.WriteSingle((int)DSROffsets.PlayerCtrl.WarpY, position.Y);
            _playerCtrl.WriteSingle((int)DSROffsets.PlayerCtrl.WarpZ, position.Z);
            _playerCtrl.WriteSingle((int)DSROffsets.PlayerCtrl.WarpAngle, position.Angle);
            _playerCtrl.WriteBoolean((int)DSROffsets.PlayerCtrl.Warp, true);
        }

        internal struct Position
        {
            internal float X;
            internal float Y;
            internal float Z;
            internal float Angle;

            internal Position(float x, float y, float z, float angle)
            {
                X = x;
                Y = y;
                Z = z;
                Angle = angle;
            }
        }
    }
}
