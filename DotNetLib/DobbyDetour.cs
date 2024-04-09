﻿namespace DotNetLib
{
    // Taken From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Hook/Dobby/DobbyDetour.cs
    public class DobbyDetour : BaseNativeDetour<DobbyDetour>
    {
        public DobbyDetour(nint originalMethodPtr, Delegate detourMethod) : base(originalMethodPtr, detourMethod)
        {
        }

        protected override void ApplyImpl()
        {
            DobbyLib.Commit(OriginalMethodPtr);
        }

        protected override unsafe void PrepareImpl()
        {
            nint trampolinePtr = 0;
            DobbyLib.Prepare(OriginalMethodPtr, DetourMethodPtr, &trampolinePtr);
            TrampolinePtr = trampolinePtr;
        }

        protected override void UndoImpl()
        {
            DobbyLib.Destroy(OriginalMethodPtr);
        }

        protected override void FreeImpl()
        {
        }
    }
}