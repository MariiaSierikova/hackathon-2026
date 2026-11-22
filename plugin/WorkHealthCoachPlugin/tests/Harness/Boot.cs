using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// Loads PluginApi.dll (and anything it needs) from next to the harness or from the installed Logi Plugin Service.
internal static class Boot
{
    [ModuleInitializer]
    public static void Init()
    {
        var dirs = new[]
        {
            AppContext.BaseDirectory,
            "/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/",
            "/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/.xamarin/osx-arm64/",
            "/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/.xamarin/osx-x64/",
            @"C:\Program Files\Logi\LogiPluginService\",
        };
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            foreach (var d in dirs)
            {
                var path = Path.Combine(d, name.Name + ".dll");
                if (File.Exists(path))
                {
                    return ctx.LoadFromAssemblyPath(path);
                }
            }

            return null;
        };

        // native libraries (libSkiaSharp.dylib, ...) from the same places
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (asm, libName) =>
        {
            foreach (var d in dirs)
            {
                foreach (var candidate in new[] { libName, "lib" + libName + ".dylib", libName + ".dylib", libName + ".dll" })
                {
                    var path = Path.Combine(d, candidate);
                    if (File.Exists(path) && System.Runtime.InteropServices.NativeLibrary.TryLoad(path, out var h))
                    {
                        return h;
                    }
                }
            }

            return IntPtr.Zero;
        };
    }
}
