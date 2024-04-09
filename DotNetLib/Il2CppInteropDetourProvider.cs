using Il2CppInterop.Runtime.Injection;

namespace DotNetLib
{
    // Taken From: https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Hook/Il2CppInteropDetourProvider.cs
    public class Il2CppInteropDetourProvider : IDetourProvider
    {
        public IDetour Create<TDelegate>(nint original, TDelegate target) where TDelegate : Delegate
        {
            return new Il2CppInteropDetour(INativeDetour.Create(original, target));
        }
    }

    internal class Il2CppInteropDetour : IDetour
    {
        private readonly INativeDetour detour;

        public Il2CppInteropDetour(INativeDetour detour)
        {
            this.detour = detour;
        }

        public nint Target => detour.OriginalMethodPtr;
        public nint Detour => detour.DetourMethodPtr;
        public nint OriginalTrampoline => detour.TrampolinePtr;

        public void Dispose()
        {
            detour.Dispose();
        }

        public void Apply()
        {
            detour.Apply();
        }

        public T GenerateTrampoline<T>() where T : Delegate
        {
            return detour.GenerateTrampoline<T>();
        }
    }
}