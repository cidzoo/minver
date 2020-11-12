using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Bullseye;
using Xunit;
using static Bullseye.Targets;
using static MinVerTests.Infra.FileSystem;
using static MinVerTests.Infra.Git;
using static SimpleExec.Command;

var testPackageBaseOutput = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

var buildNumber = 1;

var cmd = new RootCommand()
{
    new Option<string>("--package-tests-sdk", "The SDK version to use for package tests"),
    new Option<bool>("--package-tests-suppress-nologo", "Suppress --nologo when running package tests"),
    new Option<bool>("--skip-pack-dependency", "Skip the \"pack\" target as a dependency"),
};

cmd.Add(new Argument("targets") { Arity = ArgumentArity.ZeroOrMore, Description = "The targets to run or list." });
foreach (var option in Options.Definitions)
{
    cmd.Add(new Option(new[] { option.ShortName, option.LongName }.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray(), option.Description));
}

cmd.Handler = CommandHandler.Create<string, bool, bool>((packageTestsSdk, packageTestsSuppressNoLogo, skipPackDependency) =>
{
    var cmdLine = cmd.Parse(args);
    var targets = cmdLine.CommandResult.Tokens.Select(token => token.Value);
    var options = new Options(Options.Definitions.Select(o => (o.LongName, cmdLine.ValueForOption<bool>(o.LongName))));

    Run(targets, options, packageTestsSdk, packageTestsSuppressNoLogo, skipPackDependency);
});

return cmd.Invoke(args);

void Run(IEnumerable<string> targets, Options options, string packageTestsSdk, bool packageTestsSuppressNoLogo, bool skipPackDependency)
{
    Target("build", () => RunAsync("dotnet", "build --configuration Release --nologo --verbosity quiet"));

    Target(
        "test-api",
        DependsOn("build"),
        () => RunAsync("dotnet", "test --configuration Release --no-build --nologo"));

    Target(
        "publish",
        DependsOn("build"),
        () => RunAsync("dotnet", "publish ./MinVer/MinVer.csproj --configuration Release --no-build --nologo"));

    Target(
        "pack",
        DependsOn("publish"),
        ForEach("./MinVer/MinVer.csproj", "./minver-cli/minver-cli.csproj"),
        project => RunAsync("dotnet", $"pack {project} --configuration Release --no-build --nologo"));

    string testProject = default;

    Target(
        "create-test-project",
        skipPackDependency ? default : DependsOn("pack"),
        async () =>
        {
            testProject = GetScenarioDirectory("package");
            EnsureEmptyDirectory(testProject);

            var source = Path.GetFullPath("./MinVer/bin/Release/");
            var version = Path.GetFileNameWithoutExtension(Directory.EnumerateFiles(source, "*.nupkg").First()).Split("MinVer.", 2)[1];

            if (!string.IsNullOrWhiteSpace(packageTestsSdk))
            {
                File.WriteAllText(
                    Path.Combine(testProject, "global.json"),
$@"{{
  ""sdk"": {{
    ""version"": ""{packageTestsSdk.Trim()}"",
    ""rollForward"": ""disable""
  }}
}}
");
            }

            await RunAsync("dotnet", "new classlib", testProject);
            await RunAsync("dotnet", $"add package MinVer --source {source} --version {version} --package-directory packages", testProject);
            await RunAsync("dotnet", $"restore --source {source} --packages packages", testProject);
        });

    Target(
        "test-package-no-repo",
        DependsOn("create-test-project"),
        async () =>
        {
            // arrange
            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-no-repo");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo);

            // assert
            AssertVersion(new Version(0, 0, 0, new[] { "alpha", "0" }), output);
        });

    Target(
        "test-package-no-commits",
        DependsOn("test-package-no-repo"),
        async () =>
        {
            // arrange
            Init(testProject);

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-no-commits");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo);

            // assert
            AssertVersion(new Version(0, 0, 0, new[] { "alpha", "0" }), output);
        });

    Target(
        "test-package-commit",
        DependsOn("test-package-no-commits"),
        async () =>
        {
            // arrange
            PrepareForCommits(testProject);
            Commit(testProject);

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-commit");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo);

            // assert
            AssertVersion(new Version(0, 0, 0, new[] { "alpha", "0" }), output);
        });

    Target(
        "test-package-non-version-tag",
        DependsOn("test-package-commit"),
        async () =>
        {
            // arrange
            Tag(testProject, "foo");

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-non-version-tag");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo);

            // assert
            AssertVersion(new Version(0, 0, 0, new[] { "alpha", "0" }), output);
        });

    Target(
        "test-package-version-tag",
        DependsOn("test-package-non-version-tag"),
        async () =>
        {
            // arrange
            Tag(testProject, "v.1.2.3+foo");

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-version-tag");

            // act
            await CleanAndPack(testProject, output, "normal", packageTestsSuppressNoLogo, env => env.Add("MinVerTagPrefix", "v."));

            // assert
            AssertVersion(new Version(1, 2, 3, default, default, "foo"), output);
        });

    Target(
        "test-package-commit-after-tag",
        DependsOn("test-package-version-tag"),
        async () =>
        {
            // arrange
            Tag(testProject, "1.2.3+foo");
            Commit(testProject);

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-commit-after-tag");

            // act
            await CleanAndPack(testProject, output, "detailed", packageTestsSuppressNoLogo);

            // assert
            AssertVersion(new Version(1, 2, 4, new[] { "alpha", "0" }, 1), output);
        });

    Target(
        "test-package-non-default-auto-increment",
        DependsOn("test-package-commit-after-tag"),
        async () =>
        {
            // arrange
            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-non-default-auto-increment");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo, env => env.Add("MinVerAutoIncrement", "minor"));

            // assert
            AssertVersion(new Version(1, 3, 0, new[] { "alpha", "0" }, 1), output);
        });

    Target(
        "test-package-annotated-tag",
        DependsOn("test-package-non-default-auto-increment"),
        async () =>
        {
            // arrange
            AnnotatedTag(testProject, "1.4.0", "foo");

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-annotated-tag");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo);

            // assert
            AssertVersion(new Version(1, 4, 0), output);
        });

    Target(
        "test-package-minimum-major-minor-on-tag",
        DependsOn("test-package-annotated-tag"),
        async () =>
        {
            // arrange
            Commit(testProject);
            Tag(testProject, "1.5.0");

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-minimum-major-minor-on-tag");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo, env => env.Add("MinVerMinimumMajorMinor", "2.0"));

            // assert
            AssertVersion(new Version(2, 0, 0, new[] { "alpha", "0" }), output);
        });

    Target(
        "test-package-minimum-major-minor-after-tag",
        DependsOn("test-package-minimum-major-minor-on-tag"),
        async () =>
        {
            // arrange
            Commit(testProject);

            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-minimum-major-minor-after-tag");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo, env => env.Add("MinVerMinimumMajorMinor", "2.0"));

            // assert
            AssertVersion(new Version(2, 0, 0, new[] { "alpha", "0" }, 1), output);
        });

    Target(
        "test-package-default-pre-release-phase",
        DependsOn("test-package-minimum-major-minor-after-tag"),
        async () =>
        {
            // arrange
            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-default-pre-release-phase");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo, env => env.Add("MinVerDefaultPreReleasePhase", "preview"));

            // assert
            AssertVersion(new Version(1, 5, 1, new[] { "preview", "0" }, 1), output);
        });

    Target(
        "test-package-version-override",
        DependsOn("test-package-default-pre-release-phase"),
        async () =>
        {
            // arrange
            var output = Path.Combine(testPackageBaseOutput, $"{buildNumber}-test-package-version-override");

            // act
            await CleanAndPack(testProject, output, "diagnostic", packageTestsSuppressNoLogo, env => env.Add("MinVerVersionOverride", "3.2.1-rc.4+build.5"));

            // assert
            AssertVersion(new Version(3, 2, 1, new[] { "rc", "4" }, default, "build.5"), output);
        });

    Target("test-package", DependsOn("test-package-version-override"));

    Target("default", DependsOn("test-api", "test-package"));

    RunTargetsAndExit(targets, options);
}

async Task CleanAndPack(string path, string output, string verbosity, bool suppressNoLogo, Action<IDictionary<string, string>> configureEnvironment = null)
{
    EnsureEmptyDirectory(output);

    var noLogo = suppressNoLogo ? "" : " --nologo";

    await RunAsync("dotnet", $"build --no-restore{noLogo}", path, configureEnvironment: configureEnvironment);
    await RunAsync(
        "dotnet",
        $"pack --no-build --output {output}{noLogo}",
        path,
        configureEnvironment: env =>
        {
            configureEnvironment?.Invoke(env);
            env.Add("MinVerBuildMetadata", $"build.{buildNumber++}");
            env.Add("MinVerVerbosity", verbosity ?? "");
            env.Add("NoPackageAnalysis", "true");
        });
}

static void AssertVersion(Version expected, string path)
{
    var packagePath = Directory.EnumerateFiles(path, "*.nupkg", new EnumerationOptions { RecurseSubdirectories = true })
        .First();

    Assert.EndsWith(expected.ToString().Split('+')[0], Path.GetFileNameWithoutExtension(packagePath));

    ZipFile.ExtractToDirectory(
        packagePath,
        Path.Combine(Path.GetDirectoryName(packagePath), Path.GetFileNameWithoutExtension(packagePath)));

    var assemblyPath = Directory.EnumerateFiles(path, "*.dll", new EnumerationOptions { RecurseSubdirectories = true })
        .First();

    var context = new AssemblyLoadContext(default, true);
    var assembly = context.LoadFromAssemblyPath(assemblyPath);
    var assemblyVersion = assembly.GetName().Version;
    context.Unload();

    var fileVersion = FileVersionInfo.GetVersionInfo(assemblyPath);

    Assert.Equal(expected.Major, assemblyVersion.Major);
    Assert.Equal(0, assemblyVersion.Minor);
    Assert.Equal(0, assemblyVersion.Build);

    Assert.Equal(expected.Major, fileVersion.FileMajorPart);
    Assert.Equal(expected.Minor, fileVersion.FileMinorPart);
    Assert.Equal(expected.Patch, fileVersion.FileBuildPart);

    Assert.Equal(expected.ToString(), fileVersion.ProductVersion);
}

public record Version
{
    public Version(int major, int minor, int patch, IEnumerable<string> preReleaseIdentifiers = null, int height = 0, string buildMetadata = null) =>
        (this.Major, this.Minor, this.Patch, this.PreReleaseIdentifiers, this.Height, this.BuildMetadata) =
            (major, minor, patch, preReleaseIdentifiers?.ToList() ?? new List<string>(), height, buildMetadata);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    private List<string> PreReleaseIdentifiers { get; }
    private int Height { get; }
    private string BuildMetadata { get; }

    public override string ToString() =>
        $"{this.Major}.{this.Minor}.{this.Patch}{(this.PreReleaseIdentifiers.Count == 0 ? "" : $"-{string.Join(".", this.PreReleaseIdentifiers)}")}{(this.Height == 0 ? "" : $".{this.Height}")}{(string.IsNullOrEmpty(this.BuildMetadata) ? "" : $"+{this.BuildMetadata}")}";
}
