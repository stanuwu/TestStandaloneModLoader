using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using Il2CppInterop.HarmonySupport;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Startup;
using LibCpp2IL;
using Microsoft.Extensions.Logging;

namespace DotNetLib
{
    public static class Entry
    {
        private const string TRUSTED_PLATFORM_ASSEMBLIES = "TRUSTED_PLATFORM_ASSEMBLIES";
        private const string UnityDownloadUrl = "https://unity.bepinex.dev/libraries/{VERSION}.zip";

        private const string GameAssemblyPath = "./GameAssembly.dll";
        private const string DecryptedMetaPath = "./decrypted-metadata.dat";
        private const string InteropDllsPath = "./InteropDlls";
        private const string UnityLibsPath = "./UnityEngine";
        private const string HashPath = "./AssemblyHash";

        // 2021, 3, 14, 57736
        private static readonly UnityVersion UnityVersion = new(2021, 3, 14);

        private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("Pre");

        private static Harmony HarmonyInstance;

        public static int Init(IntPtr arg, int argLength)
        {
            _logger.LogInformation("Runtime Loaded (this is c#)");
            _logger.LogInformation("Initialising");

            HarmonyInstance = new Harmony("DotNetLib");

            NativeLibrary.SetDllImportResolver(typeof(IL2CPP).Assembly, DllImportResolver);

            Environment.SetEnvironmentVariable("IL2CPP_INTEROP_DATABASES_LOCATION", InteropDllsPath);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveInteropAssemblies;
            InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);

            if (!GenerateInteropAssemblies()) return 1;

            var runtime = Il2CppInteropRuntime.Create(new RuntimeConfiguration
                {
                    UnityVersion = new Version(UnityVersion.Major, UnityVersion.Minor, UnityVersion.Build),
                    DetourProvider = new Il2CppInteropDetourProvider()
                })
                .AddLogger(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("Interop"))
                .AddHarmonySupport();

            _logger.LogInformation("Runtime Created");

            Attach();

            _logger.LogInformation("Runtime Starting");

            runtime.Start();

            _logger.LogInformation("Runtime Started");

            var interop = new Il2CppEntryPoint();
            interop.Init();

            return 0;
        }

        public static void Attach()
        {
            var baseObject = new BaseObject();
            baseObject.Init();
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

                // Load Unity Libs and Download Unity
                AppDomain.CurrentDomain.AddCecilPlatformAssemblies(UnityLibsPath);
                DownloadUnity();

                // Dump Dummy Dlls
                _logger.LogInformation("Running Cpp2Il");
                Cpp2IlApi.InitializeLibCpp2Il(GameAssemblyPath, DecryptedMetaPath, UnityVersion);
                List<Cpp2IlProcessingLayer> processingLayers = new() { new AttributeInjectorProcessingLayer() };

                foreach (var cpp2IlProcessingLayer in processingLayers) cpp2IlProcessingLayer.PreProcess(Cpp2IlApi.CurrentAppContext, processingLayers);

                foreach (var cpp2IlProcessingLayer in processingLayers) cpp2IlProcessingLayer.Process(Cpp2IlApi.CurrentAppContext);

                var assemblies = new AsmResolverDummyDllOutputFormat().BuildAssemblies(Cpp2IlApi.CurrentAppContext);

                LibCpp2IlMain.Reset();
                Cpp2IlApi.CurrentAppContext = null;

                // Load Dumped Assemblies
                var cecilAssemblies = new AsmToCecilConverter(assemblies).ConvertAll();
                var opts = new GeneratorOptions
                {
                    GameAssemblyPath = GameAssemblyPath,
                    Source = cecilAssemblies,
                    OutputDir = InteropDllsPath,
                    UnityBaseLibsDir = UnityLibsPath
                };

                _logger.LogInformation("Generating Interop Assemblies");

                Il2CppInteropGenerator.Create(opts).AddLogger(_logger)
                    .AddInteropAssemblyGenerator()
                    .Run();

                cecilAssemblies.ForEach(x => x.Dispose());

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
            var source = UnityDownloadUrl.Replace("{VERSION}", $"{UnityVersion.Major}.{UnityVersion.Minor}.{UnityVersion.Build}");

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

            // Hash some common dependencies as they can affect output
            HashString(md5, typeof(InteropAssemblyGenerator).Assembly.GetName().Version.ToString());
            HashString(md5, typeof(Cpp2IlApi).Assembly.GetName().Version.ToString());

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