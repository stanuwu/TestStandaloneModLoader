using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

namespace DotNetLib
{
    // Taken From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Hook/BaseNativeDetour.cs
    public abstract class BaseNativeDetour<T> : INativeDetour where T : BaseNativeDetour<T>
    {
        protected BaseNativeDetour(nint originalMethodPtr, Delegate detourMethod)
        {
            OriginalMethodPtr = originalMethodPtr;
            DetourMethod = detourMethod;
            DetourMethodPtr = Marshal.GetFunctionPointerForDelegate(detourMethod);
        }

        public bool IsPrepared { get; protected set; }
        protected MethodInfo TrampolineMethod { get; set; }
        protected Delegate DetourMethod { get; set; }
        public bool IsValid { get; private set; } = true;
        public bool IsApplied { get; private set; }

        public nint OriginalMethodPtr { get; }
        public nint DetourMethodPtr { get; }
        public nint TrampolinePtr { get; protected set; }

        public void Dispose()
        {
            if (!IsValid) return;
            Undo();
            Free();
        }

        public void Apply()
        {
            if (IsApplied) return;

            Prepare();
            ApplyImpl();

            IsApplied = true;
        }

        public TDelegate GenerateTrampoline<TDelegate>() where TDelegate : Delegate
        {
            if (!typeof(Delegate).IsAssignableFrom(typeof(TDelegate)))
                throw new InvalidOperationException($"Type {typeof(TDelegate)} not a delegate type.");

            _ = GenerateTrampoline(typeof(TDelegate).GetMethod("Invoke"));

            return Marshal.GetDelegateForFunctionPointer<TDelegate>(TrampolinePtr);
        }

        public void Undo()
        {
            if (IsApplied && IsPrepared) UndoImpl();
        }

        public void Free()
        {
            FreeImpl();
            IsValid = false;
        }

        public MethodBase GenerateTrampoline(MethodBase signature = null)
        {
            if (TrampolineMethod == null)
            {
                Prepare();
                TrampolineMethod = DetourHelper.GenerateNativeProxy(TrampolinePtr, signature);
            }

            return TrampolineMethod;
        }

        protected abstract void ApplyImpl();

        private void Prepare()
        {
            if (IsPrepared) return;
            PrepareImpl();
            IsPrepared = true;
        }

        protected abstract void PrepareImpl();

        protected abstract void UndoImpl();

        protected abstract void FreeImpl();
    }
}