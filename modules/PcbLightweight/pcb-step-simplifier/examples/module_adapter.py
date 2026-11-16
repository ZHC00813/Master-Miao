"""Subprocess adapter; keep plan review between analyze() and build()."""
import json
import os
from pathlib import Path
import subprocess
import sys

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts' / 'pcb_simplify.py'


def _run(arguments, *, python_executable=None, ocp_path=None, timeout=600):
    command = [str(python_executable or sys.executable), str(SCRIPT)]
    if ocp_path is not None:
        command.extend(['--ocp-path', str(Path(ocp_path).resolve())])
    command.extend(map(str, arguments))
    environment = dict(os.environ, PYTHONUTF8='1', PYTHONIOENCODING='utf-8')
    result = subprocess.run(command, capture_output=True, text=True,
                            encoding='utf-8', errors='replace',
                            env=environment, timeout=timeout, shell=False)
    if result.returncode:
        raise RuntimeError(f'PCB simplification stopped (exit {result.returncode}).\n'
                           f'{result.stdout[-12000:]}\n{result.stderr[-4000:]}')
    return result


def analyze(input_path, work_dir, *, keep_largest=5, keep_pattern='',
            board_pattern='', python_executable=None, ocp_path=None, timeout=600):
    work = Path(work_dir).resolve()
    arguments = ['analyze', str(Path(input_path).resolve()), '--work', str(work),
                 '--keep-largest', str(keep_largest)]
    if keep_pattern:
        arguments.extend(['--keep-pattern', keep_pattern])
    if board_pattern:
        arguments.extend(['--board-pattern', board_pattern])
    _run(arguments, python_executable=python_executable, ocp_path=ocp_path, timeout=timeout)
    return json.loads((work / 'plan.json').read_text(encoding='utf-8'))


def build(work_dir, output_path, *, max_gap=0.2, bridge_radius=0.03,
          pcurves=False, python_executable=None, ocp_path=None, timeout=600):
    work = Path(work_dir).resolve()
    arguments = ['build', '--work', str(work), '--output', str(Path(output_path).resolve()),
                 '--max-gap', str(max_gap), '--bridge-radius', str(bridge_radius)]
    if pcurves:
        arguments.append('--pcurves')
    _run(arguments, python_executable=python_executable, ocp_path=ocp_path, timeout=timeout)
    return json.loads((work / 'report.json').read_text(encoding='utf-8'))
