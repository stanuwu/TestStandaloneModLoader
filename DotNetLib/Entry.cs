using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AssemblyUnhollower;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.Startup;
using LibCpp2IL;
using Microsoft.Extensions.Logging;
using UnhollowerBaseLib;
using UnhollowerBaseLib.Runtime;

namespace DotNetLib
{
    public static class Entry
    {
        private const string TRUSTED_PLATFORM_ASSEMBLIES = "TRUSTED_PLATFORM_ASSEMBLIES";
        private const string UnityDownloadUrl = "https://unity.bepinex.dev/libraries/{VERSION}.zip";

        private const string Il2CppConfig =
            "{\"DumpMethod\": true, \"DumpField\": true, \"DumpProperty\": true, \"DumpAttribute\": true, \"DumpFieldOffset\": true, \"DumpMethodOffset\": true, \"DumpTypeDefIndex\": true, \"GenerateDummyDll\": true, \"GenerateStruct\": true, \"DummyDllAddToken\": true, \"RequireAnyKey\": false, \"ForceIl2CppVersion\": false, \"ForceVersion\": 16, \"ForceDump\": false, \"NoRedirectedPointer\": false }";

        private const string GameAssemblyPath = "./GameAssembly.dll";
        private const string Il2CppDumperPath = "./Dumper/Il2CppDumper.exe";
        private const string Il2CppConfigPath = "./Dumper/config.json";
        private const string DecryptedMetaPath = "./decrypted-metadata.dat";
        private const string DummyDllsOutputPath = "./DummyDlls";
        private const string DummyDllsPath = "./DummyDlls/DummyDll";
        private const string InteropDllsPath = "./InteropDlls";
        private const string UnityLibsPath = "./UnityEngine";
        private const string HashPath = "./AssemblyHash";
        private const string MsCorLibPath = "./mscorlib.dll";

        // 2021, 3, 14, 57736
        private static readonly int[] UnityVersion = { 2021, 3, 14 };

        private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("Pre");

        private static Harmony HarmonyInstance;

        public static int Init(IntPtr arg, int argLength)
        {
            _logger.LogInformation("Runtime Loaded (this is c#)");
            _logger.LogInformation("Initialising");

            // HarmonyInstance = new Harmony("DotNetLib");

            Environment.SetEnvironmentVariable("IL2CPP_INTEROP_DATABASES_LOCATION", InteropDllsPath);
            InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);

            UnityVersionHandler.Initialize(UnityVersion[0], UnityVersion[1], UnityVersion[2]);
            if (!GenerateInteropAssemblies()) return 1;

            NativeLibrary.SetDllImportResolver(typeof(IL2CPP).Assembly, DllImportResolver);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveInteropAssemblies;

            var runtime = Il2CppInteropRuntime.Create(new RuntimeConfiguration
                {
                    UnityVersion = new Version(UnityVersion[0], UnityVersion[1], UnityVersion[2])
                })
                .AddLogger(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("Interop"));

            _logger.LogInformation("Runtime Created");
            _logger.LogInformation("Runtime Starting");

            runtime.Start();

            _logger.LogInformation("Runtime Started");

            Attach();

            return 0;
        }

        public static void Attach()
        {
            var baseObject = new BaseObject();
        }

        private static bool GenerateInteropAssemblies()
        {
            if (!CheckIfGenerationRequired())
            {
                _logger.LogInformation("Skipping Generation");
                return true;
            }

            try
            {
                // Create Directory
                Directory.CreateDirectory(InteropDllsPath);
                foreach (var enumerateFile in Directory.EnumerateFiles(InteropDllsPath, "*.dll")) File.Delete(enumerateFile);

                // Dump Dummy Dll With Il2CppDumper
                _logger.LogInformation("Running Il2CppDumper");
                File.WriteAllText(Il2CppConfigPath, Il2CppConfig);
                Directory.CreateDirectory(DummyDllsOutputPath);
                var p = Process.Start(Il2CppDumperPath, $"{GameAssemblyPath} {DecryptedMetaPath} {DummyDllsOutputPath}");
                while (!p.HasExited) Thread.Sleep(50);
                p.CloseMainWindow();
                p.Close();
                _logger.LogInformation("Dumped Dummy Assemblies");
                _logger.LogInformation("Loading");

                // Load Dumped Assemblies

                var opts = new UnhollowerOptions
                {
                    SourceDir = DummyDllsPath,
                    OutputDir = InteropDllsPath,
                    MscorlibPath = MsCorLibPath
                };

                _logger.LogInformation("Generating Interop Assemblies");

                Program.Main(opts);

                // Write Hash
                File.WriteAllText(HashPath, ComputeHash());
            }
            catch (Exception e)
            {
                _logger.LogError("Error Generating Interop Assemblies");
                _logger.LogError(e.Message);
                _logger.LogError(e.StackTrace);
                return false;
            }

            return true;
        }

        // Taken From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs
        private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            _logger.LogWarning($"!! Resolving Dll: {libraryName}");

            if (libraryName == "GameAssembly") return NativeLibrary.Load(GameAssemblyPath, assembly, searchPath);

            return IntPtr.Zero;
        }

        // Taken From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs
        private static Assembly ResolveInteropAssemblies(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);

            _logger.LogWarning($"!! Resolving Assembly: {assemblyName}");

            var resolved = TryResolveDllAssembly(assemblyName, InteropDllsPath, out var foundAssembly)
                ? foundAssembly
                : null;

            if (resolved == null)
                resolved = TryResolveDllAssembly(assemblyName, "./", out var foundAssembly2)
                    ? foundAssembly2
                    : null;

            if (resolved == null) _logger.LogError("ASSEMBLY FAILED TO RESOLVE (NULL)");

            return resolved;
        }

        // Taken From https://github.com/BepInEx/BepInEx/blob/master/BepInEx.Core/Utility.cs
        public static bool TryResolveDllAssembly(AssemblyName assemblyName, string directory, out Assembly assembly)
        {
            return TryResolveDllAssembly(assemblyName, directory, Assembly.LoadFrom, out assembly);
        }

        // Take From https://github.com/BepInEx/BepInEx/blob/master/BepInEx.Core/Utility.cs
        public static bool TryResolveDllAssembly<T>(AssemblyName assemblyName,
            string directory,
            Func<string, T> loader,
            out T assembly) where T : class
        {
            assembly = null;

            var potentialDirectories = new List<string> { directory };

            if (!Directory.Exists(directory))
                return false;

            potentialDirectories.AddRange(Directory.GetDirectories(directory, "*", SearchOption.AllDirectories));

            foreach (var subDirectory in potentialDirectories)
            {
                var potentialPaths = new[]
                {
                    $"{assemblyName.Name}.dll",
                    $"{assemblyName.Name}.exe"
                };

                foreach (var potentialPath in potentialPaths)
                {
                    var path = Path.Combine(subDirectory, potentialPath);

                    _logger.LogWarning($"Attempting Load: {path}");

                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        assembly = loader(path);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        // Abbreviated From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs
        private static bool CheckIfGenerationRequired()
        {
            if (!Directory.Exists(InteropDllsPath) || Directory.GetFiles(InteropDllsPath).Length == 0)
                return true;

            if (!File.Exists(HashPath))
                return true;

            if (ComputeHash() != File.ReadAllText(HashPath))
            {
                _logger.LogInformation("Detected outdated interop assemblies, will regenerate them now");
                return true;
            }

            return false;
        }

        // Abbreviated From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs
        private static void DownloadUnity()
        {
            _logger.LogInformation("Downloading Unity");
            var source = UnityDownloadUrl.Replace("{VERSION}", $"{UnityVersion[0]}.{UnityVersion[1]}.{UnityVersion[2]}");

            Directory.CreateDirectory(UnityLibsPath);
            foreach (var enumerateFile in Directory.EnumerateFiles(UnityLibsPath, "*.dll")) File.Delete(enumerateFile);

            using var httpClient = new HttpClient();
            using var zipStream = httpClient.GetStreamAsync(source).GetAwaiter().GetResult();
            using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            _logger.LogInformation("Extracting downloaded unity base libraries");
            zipArchive.ExtractToDirectory(UnityLibsPath);
        }

        // Taken From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs
        private static string ComputeHash()
        {
            using var md5 = MD5.Create();

            static void HashFile(ICryptoTransform hash, string file)
            {
                const int defaultCopyBufferSize = 81920;
                using var fs = File.OpenRead(file);
                var buffer = new byte[defaultCopyBufferSize];
                int read;
                while ((read = fs.Read(buffer)) > 0)
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
            }

            static void HashString(ICryptoTransform hash, string str)
            {
                var buffer = Encoding.UTF8.GetBytes(str);
                hash.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
            }

            HashFile(md5, GameAssemblyPath);

            if (Directory.Exists(UnityLibsPath))
                foreach (var file in Directory.EnumerateFiles(UnityLibsPath, "*.dll",
                             SearchOption.TopDirectoryOnly))
                {
                    HashString(md5, Path.GetFileName(file));
                    HashFile(md5, file);
                }

            md5.TransformFinalBlock(new byte[0], 0, 0);

            return ByteArrayToString(md5.Hash);
        }

        // Taken From https://github.com/BepInEx/BepInEx/blob/master/BepInEx.Core/Utility.cs
        public static string ByteArrayToString(byte[] data)
        {
            var builder = new StringBuilder(data.Length * 2);

            foreach (var b in data)
                builder.AppendFormat("{0:x2}", b);

            return builder.ToString();
        }

        // Taken From https://github.com/BepInEx/BepInEx/blob/master/BepInEx.Core/Utility.cs
        internal static void AddCecilPlatformAssemblies(this AppDomain appDomain, string assemblyDir)
        {
            if (!Directory.Exists(assemblyDir))
                return;
            // Cecil 0.11 requires one to manually set up list of trusted assemblies for assembly resolving
            var curTrusted = appDomain.GetData(TRUSTED_PLATFORM_ASSEMBLIES) as string;
            var addTrusted = string.Join(Path.PathSeparator.ToString(),
                Directory.GetFiles(assemblyDir, "*.dll",
                    SearchOption.TopDirectoryOnly));
            var newTrusted = curTrusted == null ? addTrusted : $"{curTrusted}{Path.PathSeparator}{addTrusted}";
            appDomain.SetData(TRUSTED_PLATFORM_ASSEMBLIES, newTrusted);
        }
    }
}