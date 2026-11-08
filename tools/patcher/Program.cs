using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Rewrites BepInEx.Unity.Mono.Preloader.UnityPreloaderRunner.PreloaderPreMain so that,
// instead of running BepInEx's platform probe (which dies on Unity 6's missing
// Module.GetPEKind), it calls our own boot shim.
//
// Doorstop already invokes Doorstop.Entrypoint.Start() in this assembly; that method
// looks PreloaderPreMain up by reflection and invokes it. Redirecting that one method
// reuses the one injection path this Unity runtime actually accepts.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: beppatch <preloaderIn> <preloaderOut>");
    return 2;
}

var input = args[0];
var output = args[1];

var asm = AssemblyDefinition.ReadAssembly(input);
var mod = asm.MainModule;

var type = mod.GetType("BepInEx.Unity.Mono.Preloader.UnityPreloaderRunner");
if (type == null)
{
    Console.Error.WriteLine("ERROR: UnityPreloaderRunner not found");
    return 3;
}

var method = type.Methods.FirstOrDefault(m => m.Name == "PreloaderPreMain");
if (method == null)
{
    Console.Error.WriteLine("ERROR: PreloaderPreMain not found");
    return 4;
}

// Add (or reuse) the reference to our boot shim and point the body at Loader.Run().
var bootRef = mod.AssemblyReferences.FirstOrDefault(r => r.Name == "FamilyTimeChef.Boot");
if (bootRef == null)
{
    bootRef = new AssemblyNameReference("FamilyTimeChef.Boot", new Version(1, 0, 0, 0));
    mod.AssemblyReferences.Add(bootRef);
}

var loaderType = new TypeReference("FamilyTimeChef.Boot", "Loader", mod, bootRef, false);
var runRef = new MethodReference("Run", mod.TypeSystem.Void, loaderType) { HasThis = false };

method.Body = new MethodBody(method);
method.Body.MaxStackSize = 1;
method.Body.InitLocals = false;

var il = method.Body.GetILProcessor();
il.Emit(OpCodes.Call, runRef);
il.Emit(OpCodes.Ret);

asm.Write(output);
Console.WriteLine("patched -> " + output);
return 0;
