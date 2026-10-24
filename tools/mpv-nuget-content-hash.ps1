# 直接调用当前固定 SDK 自带 NuGet 实现；实际包内容须绑定固定 restore 摘要，不信可修改旁文件。
function Get-MpvNuGetPackageContentHash([string]$Archive) {
    Assert-PlainPath $Archive
    $toolRepository=(Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
    $pin=(ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Get-ChildPath $toolRepository 'global.json')))).sdk.version
    if ($pin -isnot [string] -or $pin -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { Fail-Payload '无法定位固定 SDK 的 NuGet 内容哈希实现。' }
    $roots=@($env:DOTNET_ROOT,(Join-Path $env:USERPROFILE '.dotnet'),(Join-Path $env:ProgramFiles 'dotnet')) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    $desktop=$PSVersionTable.PSEdition -eq 'Desktop'
    $sdkDirectory=$null
    foreach ($root in $roots) {
        $candidate=[IO.Path]::GetFullPath((Join-Path $root ('sdk\'+$pin)))
        if ($desktop) { $candidate=Join-Path $candidate 'Sdks\Microsoft.NET.Sdk\tools\net472' }
        Assert-PlainPath $candidate
        if (Test-Path -LiteralPath (Join-Path $candidate 'NuGet.Packaging.dll') -PathType Leaf) { $sdkDirectory=$candidate;break }
    }
    if ($null-eq $sdkDirectory) { Fail-Payload "缺少固定 SDK $pin 的 NuGet.Packaging；离线停止、不回退旁文件。" }
    if ($null-eq ('Momoka.Tooling.NuGetPackageContent' -as [type])) {
        # PS5.1 的官方 net472 库需 SDK 自带 System.Memory 等版本重定向；resolver 只认明确的 managed 库。
        # C#5 语法仅为 Windows PowerShell 编译器兼容；没有另写 ZIP/hash 算法或增加产品依赖。
        $source=@'
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
namespace Momoka.Tooling
{
    public static class NuGetPackageContent
    {
        private static readonly object Gate = new object();
        private static string _root;
        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            switch (name)
            {
                case "System.Memory": case "System.Buffers": case "System.Runtime.CompilerServices.Unsafe":
                case "System.Threading.Tasks.Extensions": case "System.Collections.Immutable":
                case "NuGet.Common": case "NuGet.Versioning": case "NuGet.Frameworks": case "NuGet.Packaging":
                    string path = Path.Combine(_root, name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                default: return null;
            }
        }
        private static object Await(object value)
        {
            ((Task)value).GetAwaiter().GetResult();
            PropertyInfo result = value.GetType().GetProperty("Result");
            return result == null ? null : result.GetValue(value, null);
        }
        public static string Hash(string root, string path)
        {
            lock (Gate)
            {
                _root = root;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                try
                {
                    string library = Path.Combine(root, "NuGet.Packaging.dll");
                    Assembly assembly = Assembly.LoadFrom(library);
                    if (!String.Equals(assembly.Location, library, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("NuGet content hash library did not load from the selected SDK.");
                    Type type = assembly.GetType("NuGet.Packaging.PackageArchiveReader", true);
                    using (Stream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (IDisposable reader = (IDisposable)Activator.CreateInstance(type, new object[] { stream }))
                    {
                        // null fallback: never substitute .sha512 or .nupkg.metadata for the actual package.
                        MethodInfo method = type.GetMethod("GetContentHash", new[] { typeof(CancellationToken), typeof(Func<string>) });
                        if (method == null) throw new MissingMethodException("NuGet PackageArchiveReader.GetContentHash");
                        string hash = (string)method.Invoke(reader, new object[] { CancellationToken.None, null });
                        if (String.IsNullOrWhiteSpace(hash) || Convert.FromBase64String(hash).Length != 64)
                            throw new InvalidDataException("NuGet did not compute a SHA512 content hash.");
                        // Local archive integrity only; never verify signer chains/revocation or access the network.
                        object signature = Await(type.GetMethod("GetPrimarySignatureAsync").Invoke(reader, new object[] { CancellationToken.None }));
                        if (signature != null)
                        {
                            object content = signature.GetType().GetProperty("SignatureContent").GetValue(signature, null);
                            Await(type.GetMethod("ValidateIntegrityAsync").Invoke(reader, new[] { content, (object)CancellationToken.None }));
                        }
                        return hash;
                    }
                }
                finally { AppDomain.CurrentDomain.AssemblyResolve -= Resolve; }
            }
        }
    }
}
'@
        if ($desktop) {
            # CodeDom 的临时文件也限制在当前工具树；先核全祖先再建目录，不使用用户 TEMP。
            # CodeDom/WinPS5.1 的临时文件仍受 MAX_PATH 影响，短目录兼容深层隔离工具仓库。
            $temp=Get-ChildPath $toolRepository 'artifacts/n'
            [IO.Directory]::CreateDirectory($temp)|Out-Null
            $parameters=[CodeDom.Compiler.CompilerParameters]::new()
            $parameters.GenerateInMemory=$true
            $parameters.TempFiles=[CodeDom.Compiler.TempFileCollection]::new($temp)
            foreach($reference in @('System.dll','System.Core.dll')) { $null=$parameters.ReferencedAssemblies.Add($reference) }
            Add-Type -TypeDefinition $source -CompilerParameters $parameters
        } else { Add-Type -TypeDefinition $source }
    }
    try { return [Momoka.Tooling.NuGetPackageContent]::Hash($sdkDirectory,$Archive) }
    catch { Fail-Payload "NuGet 实际包内容校验失败：$Archive；$($_.Exception.Message)" }
}
