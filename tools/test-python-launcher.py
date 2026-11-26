"""Verify quoted test-only Python launchers and fail-closed source extraction; no product execution."""
from pathlib import Path
import argparse
import json
import shutil
import subprocess
import uuid
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--dotnet", type=Path, required=True)
parser.add_argument("--python-launcher", type=Path, required=True)
parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
args = parser.parse_args()
root = args.root.resolve()
output = root / "artifacts" / "python-launcher-tests" / uuid.uuid4().hex
output.mkdir(parents=True)
cases = []
entries = [
    ("Momoka.DanmakuPerformanceTests", "extract-ownership.py", "ExtractDanmakuLoadOwnership",
     "src/Momoka.Shell/ViewModels/PlayerViewModel.Danmaku.cs"),
    ("Momoka.DanmakuSourceBoundaryTests", "extract.py", "ExtractDanmakuBoundaries",
     "src/Momoka.Shell/Views/DanmakuPanel.xaml.cs"),
    ("Momoka.SubscriptionIdentityTests", "extract.py", "ExtractSeasonRows",
     "src/Momoka.Shell/ViewModels/MoviePilotMediaViewModel.cs"),
]


def run(project, options, log):
    result = subprocess.run([str(args.dotnet), "msbuild", str(project), *options, "-nologo"],
                            cwd=root, capture_output=True, text=True, encoding="utf-8")
    (output / log).write_text(result.stdout + result.stderr, encoding="utf-8", newline="\n")
    return result


def check(condition, name):
    cases.append({"name": name, "passed": bool(condition)})
    if not condition:
        raise AssertionError(name)
    print("PASS", name)


try:
    for name, script, target, source_path in entries:
        project = root / "tests" / name / (name + ".csproj")
        tree = ET.parse(project).getroot()
        prop = tree.find(".//PythonLauncher")
        check(prop is not None and prop.text == "py" and "== ''" in prop.get("Condition", ""),
              name + "/default-property")
        command = tree.find('.//Target[@Name="' + target + '"]/Exec').get("Command", "")
        check(command.startswith('"$(PythonLauncher)" '), name + "/quoted-executable")
        default = run(project, ["-getProperty:PythonLauncher"], name + "-default.log")
        check(default.returncode == 0 and default.stdout.strip() == "py", name + "/default-evaluation")
        override_path = str(output / "launcher with spaces" / "py.exe")
        override = run(project, ["-getProperty:PythonLauncher", "-p:PythonLauncher=" + override_path],
                       name + "-override.log")
        check(override.returncode == 0 and override.stdout.strip() == override_path, name + "/override-evaluation")
        # Use a fresh repository for both Exec failures. In the working tree an up-to-date generated
        # file can skip the target entirely, even when the configured launcher does not exist.
        fixture = output / name / "repository"
        folder = fixture / "tests" / name
        folder.mkdir(parents=True)
        (fixture / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
        (fixture / "Directory.Build.targets").write_text("<Project />", encoding="utf-8")
        fixture_project = folder / project.name
        shutil.copy2(project, fixture_project)
        shutil.copy2(project.parent / script, folder / script)
        source = fixture / source_path
        source.parent.mkdir(parents=True)
        source.write_text("wrong source extraction boundary\n", encoding="utf-8")
        missing = run(fixture_project, ["-t:" + target, "-p:PythonLauncher=" + override_path], name + "-missing.log")
        check(missing.returncode != 0 and "MSB3073" in missing.stdout + missing.stderr, name + "/missing-rejected")
        failed = run(fixture_project, ["-t:" + target, "-p:PythonLauncher=" + str(args.python_launcher)],
                     name + "-source-failure.log")
        check(failed.returncode != 0 and "MSB3073" in failed.stdout + failed.stderr
              and ("boundaries" in failed.stdout or "markers" in failed.stdout), name + "/source-failure-rejected")
finally:
    (output / "receipt.json").write_text(json.dumps(cases, ensure_ascii=False, indent=2) + "\n",
                                        encoding="utf-8", newline="\n")
print(f"Python launcher contracts {len(cases)}/{len(cases)}; evidence {output}")
