using System.Runtime.InteropServices;

namespace DotNetLib
{
    // Taken From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Hook/Dobby/DobbyLib.cs
    internal static unsafe class DobbyLib
    {
        [DllImport("./dobby/dobby", EntryPoint = "DobbyHook", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Hook(nint target, nint replacement, nint* originalCall);

        [DllImport("./dobby/dobby", EntryPoint = "DobbyPrepare", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Prepare(nint target, nint replacement, nint* originalCall);

        [DllImport("./dobby/dobby", EntryPoint = "DobbyCommit", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Commit(nint target);

        [DllImport("./dobby/dobby", EntryPoint = "DobbyDestroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Destroy(nint target);
    }
}