using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Microsoft.Extensions.Logging;

namespace DotNetLib
{
    public class Il2CppEntryPoint
    {
        private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("Internal");
        private static RuntimeInvokeDetourDelegate originalInvoke;
        private static INativeDetour RuntimeInvokeDetour { get; set; }
        public static Il2CppEntryPoint Instance { get; set; }

        public void Init()
        {
            Instance = this;
            NativeLibrary.TryLoad("GameAssembly", typeof(Il2CppEntryPoint).Assembly, null, out var il2CppHandle);
            var runtimeInvokePtr = NativeLibrary.GetExport(il2CppHandle, "il2cpp_runtime_invoke");
            RuntimeInvokeDetourDelegate invokeMethodDetour = OnInvokeMethod;
            RuntimeInvokeDetour = INativeDetour.CreateAndApply(runtimeInvokePtr, invokeMethodDetour, out originalInvoke);
        }

        private static IntPtr OnInvokeMethod(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc)
        {
            try
            {
                var methodName = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(method));

                var unhook = false;

                if (methodName == "Internal_ActiveSceneChanged")
                {
                    unhook = true;
                    _logger.LogWarning("Loading Objects");
                    Instance.Execute();
                }

                var result = originalInvoke(method, obj, parameters, exc);

                if (unhook) RuntimeInvokeDetour.Dispose();

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError("Error On Invoke Method");
                _logger.LogError(e.Message);
                _logger.LogError(e.StackTrace);
            }

            return IntPtr.Zero;
        }

        public virtual void Execute()
        {
            // executes on first scene change
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr RuntimeInvokeDetourDelegate(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc);
    }
}