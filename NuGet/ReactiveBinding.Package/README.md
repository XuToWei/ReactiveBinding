# 发布到 NuGet (Publishing)

本目录的 `ReactiveBinding.Package.csproj` 用于把 ReactiveBinding 打成 NuGet 包并发布到 [nuget.org](https://www.nuget.org/)。
它位于 `NuGet/` 目录下(在 Unity 包之外,Unity 不会编译它)。

## 包内容

`dotnet pack` 产出**单个** NuGet 包(`XuToWei.ReactiveBinding`),包含:

| 路径 | 内容 |
| --- | --- |
| `lib/netstandard2.0/ReactiveBinding.dll` | 运行时类型(`IVersion`、`IVersionSyncable`、版本容器、各 attribute) |
| `lib/netstandard2.0/ReactiveBinding.xml` | XML 文档 |
| `analyzers/dotnet/cs/ReactiveBinding.Generator.dll` | 源生成器 + 分析器 |
| `README.md` | 包说明 |

使用者只需 `dotnet add package XuToWei.ReactiveBinding`,运行时类型和生成器一并到位。

> NuGet 包面向**普通 .NET 工程**。Unity 项目继续走 UPM(git URL);如需在 Unity 里用 NuGet 包,需安装 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)。

## 前置条件

1. **包名**:`<PackageId>XuToWei.ReactiveBinding</PackageId>`(在 `ReactiveBinding.Package.csproj`)。改名只改这一行。
2. **许可证**:`<PackageLicenseExpression>MIT</PackageLicenseExpression>`,与仓库根的 `LICENSE` 一致。
3. **API Key**:在 [nuget.org](https://www.nuget.org/) → 头像 → API Keys → Create,Scope 选 *Push*,Glob 填 `XuToWei.ReactiveBinding*`。
   - 自动发布需把它存为 GitHub 仓库 Secret:**Settings → Secrets and variables → Actions → New repository secret**,名字 `NUGET_API_KEY`。

## 手动发布

```bash
# 1. 打包(版本号取自 csproj 的 <Version>)
dotnet pack NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj -c Release

# 2. 推送
dotnet nuget push NuGet/ReactiveBinding.Package/bin/Release/XuToWei.ReactiveBinding.<version>.nupkg \
  --api-key <你的KEY> \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate
```

也可在打包时用命令行覆盖版本号,不必改 csproj:

```bash
dotnet pack NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj -c Release -p:Version=1.2.3 -o artifacts
```

## 自动发布(GitHub Actions)

工作流:`.github/workflows/publish-nuget.yml`

- **触发**:推送 `v*` 标签(如 `v1.2.3`),或在 Actions 页面手动运行(`workflow_dispatch`,需填版本号)。
- **版本号**:取自标签名,自动去掉前缀 `v`(`v1.2.3` → 包版本 `1.2.3`),无需改 csproj。
- 自动执行 `dotnet pack` + `dotnet nuget push --skip-duplicate`,使用 Secret `NUGET_API_KEY`。

发版:

```bash
git tag v1.0.0
git push origin v1.0.0
```

随后在仓库的 **Actions** 页查看运行结果;成功后包会在几分钟内出现在 nuget.org。

## 版本约定

- nuget.org **不允许重复推送同一版本号**,每次发布都要递增。
- 建议 NuGet 版本与 `package.json`(UPM)的 `version` 保持一致。

## 本地验证包结构

```bash
dotnet pack NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj -c Release
unzip -l NuGet/ReactiveBinding.Package/bin/Release/XuToWei.ReactiveBinding.*.nupkg
# 应能看到 lib/netstandard2.0/ReactiveBinding.dll 与 analyzers/dotnet/cs/ReactiveBinding.Generator.dll
```
