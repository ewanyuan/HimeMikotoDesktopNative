"""Create a non-destructive PMX physics variant using Nuthouse01's PMX toolchain.

This is an asset-side experiment, not a runtime jiggle implementation.  The
original PMX is never written to.  The script only reuses the model's existing
breast bones (左胸/左胸先 and 右胸/右胸先), adds the corresponding rigid-body
chains, and lets the existing Babylon-MMD Bullet runtime solve them.

The binary PMX parsing/writing is deliberately delegated to the established
Nuthouse01 toolchain; this file contains only the model-specific configuration
and validation around it.
"""

from __future__ import annotations

import argparse
import contextlib
import copy
import hashlib
import io
import json
import math
import sys
from pathlib import Path
from typing import Iterable


def _load_toolchain(toolchain_root: Path):
    sys.path.insert(0, str(toolchain_root))
    from mmd_scripting.core import nuthouse01_pmx_parser as pmx_parser
    from mmd_scripting.core import nuthouse01_pmx_struct as pmx_struct

    return pmx_parser, pmx_struct


def _read_pmx(pmx_parser, path: Path):
    # Nuthouse01 prints progress even in quiet mode.  Keep the command output
    # machine-readable so the caller can archive the result as an audit log.
    with contextlib.redirect_stdout(io.StringIO()):
        return pmx_parser.read_pmx(str(path), moreinfo=False)


def _write_pmx(pmx_parser, path: Path, model) -> None:
    with contextlib.redirect_stdout(io.StringIO()):
        pmx_parser.write_pmx(str(path), model, moreinfo=False)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _bone_index(model, name: str) -> int:
    for index, bone in enumerate(model.bones):
        if bone.name_jp == name or bone.name_en == name:
            return index
    raise ValueError(f"Missing required breast bone: {name}")


def _weighted_points(model, bone_index: int, threshold: float = 0.5) -> list[list[float]]:
    points: list[list[float]] = []
    for vertex in model.verts:
        weight = sum(float(value) for index, value in vertex.weight if index == bone_index)
        if weight >= threshold:
            points.append(list(vertex.pos))
    if not points:
        raise ValueError(f"No vertices are weighted to bone index {bone_index}")
    return points


def _centroid(points: Iterable[list[float]]) -> list[float]:
    points = list(points)
    return [sum(point[axis] for point in points) / len(points) for axis in range(3)]


def _radius(points: Iterable[list[float]], center: list[float], lower: float, upper: float) -> float:
    distances = [
        math.sqrt(sum((point[axis] - center[axis]) ** 2 for axis in range(3)))
        for point in points
    ]
    return max(lower, min(upper, max(distances) * 0.58))


def _make_joint(pmx_struct, *, name_jp: str, name_en: str, first: int, second: int,
                position: list[float], rotation_min: list[float], rotation_max: list[float],
                rotation_spring: list[float]):
    return pmx_struct.PmxJoint(
        name_jp=name_jp,
        name_en=name_en,
        jointtype=pmx_struct.JointType.SPRING_SIXDOF,
        rb1_idx=first,
        rb2_idx=second,
        pos=list(position),
        # The existing chest joints use the PMX chest frame with a 180-degree
        # Y rotation.  Keep the same frame convention for the new side chains.
        rot=[0.0, 180.0, 0.0],
        movemin=[0.0, 0.0, 0.0],
        movemax=[0.0, 0.0, 0.0],
        movespring=[0.0, 0.0, 0.0],
        rotmin=list(rotation_min),
        rotmax=list(rotation_max),
        rotspring=list(rotation_spring),
    )


def _make_body(pmx_struct, *, name_jp: str, name_en: str, bone_index: int,
               position: list[float], radius: float, base_body, mass: float,
               move_damping: float, rotation_damping: float):
    return pmx_struct.PmxRigidBody(
        name_jp=name_jp,
        name_en=name_en,
        bone_idx=bone_index,
        pos=list(position),
        rot=[0.0, 0.0, 0.0],
        size=[radius, 0.0, 0.0],
        shape=pmx_struct.RigidBodyShape.SPHERE,
        group=base_body.group,
        nocollide_set=set(base_body.nocollide_set),
        phys_mode=pmx_struct.RigidBodyPhysMode.PHYSICS,
        phys_mass=mass,
        phys_move_damp=move_damping,
        phys_rot_damp=rotation_damping,
        phys_repel=base_body.phys_repel,
        phys_friction=base_body.phys_friction,
    )


def _create_variant(model, pmx_struct, profile: str) -> dict:
    bone_indices = {
        name: _bone_index(model, name)
        for name in ["左胸", "左胸先", "右胸", "右胸先", "胸", "胸ジンバル"]
    }

    base_body_index = next(
        (
            index
            for index, body in enumerate(model.rigidbodies)
            if body.bone_idx == bone_indices["胸"]
            and body.phys_mode == pmx_struct.RigidBodyPhysMode.PHYSICS
        ),
        None,
    )
    if base_body_index is None:
        raise ValueError("Could not find the existing dynamic chest rigid body")
    base_body = model.rigidbodies[base_body_index]

    gimbal_body_index = next(
        (
            index
            for index, body in enumerate(model.rigidbodies)
            if body.bone_idx == bone_indices["胸ジンバル"]
        ),
        None,
    )
    if gimbal_body_index is None:
        raise ValueError("Could not find the existing chest gimbal rigid body")

    # The stable profile keeps the torso/chest anchor on the animation bones.
    # Babylon-MMD's solver can otherwise settle the original whole-chest chain
    # below its authored pose.  Only the existing left/right breast bones are
    # left dynamic, with tight limits and strong springs.
    if profile == "stable":
        model.rigidbodies[base_body_index].phys_mode = pmx_struct.RigidBodyPhysMode.BONE
        model.rigidbodies[gimbal_body_index].phys_mode = pmx_struct.RigidBodyPhysMode.BONE
        root_mass, tip_mass = 0.30, 0.10
        root_move, root_rotation = 0.94, 0.90
        tip_move, tip_rotation = 0.90, 0.84
        root_limit = ([-10.0, -8.0, -10.0], [10.0, 8.0, 10.0])
        tip_limit = ([-14.0, -14.0, -14.0], [14.0, 14.0, 14.0])
        root_spring, tip_spring = [96.0, 80.0, 96.0], [28.0, 28.0, 28.0]
    # These are deliberately conservative values for the earlier asset-side
    # candidates.  They remain available for comparison, but are not the
    # default production profile.
    if profile == "balanced":
        root_mass, tip_mass = 0.60, 0.22
        root_move, root_rotation = 0.84, 0.76
        tip_move, tip_rotation = 0.78, 0.68
        root_limit = ([ -20.0, -16.0, -20.0], [20.0, 16.0, 20.0])
        tip_limit = ([ -26.0, -26.0, -26.0], [26.0, 26.0, 26.0])
        root_spring, tip_spring = [55.0, 42.0, 55.0], [14.0, 14.0, 14.0]
    elif profile == "max":
        root_mass, tip_mass = 0.45, 0.16
        root_move, root_rotation = 0.70, 0.56
        tip_move, tip_rotation = 0.66, 0.50
        root_limit = ([ -28.0, -22.0, -28.0], [28.0, 22.0, 28.0])
        tip_limit = ([ -34.0, -34.0, -34.0], [34.0, 34.0, 34.0])
        root_spring, tip_spring = [32.0, 24.0, 32.0], [7.0, 7.0, 7.0]
    elif profile != "stable":
        raise ValueError(f"Unsupported profile: {profile}")

    created = []
    for side, sign in [("左", 1.0), ("右", -1.0)]:
        root_name = f"{side}胸"
        tip_name = f"{side}胸先"
        root_points = _weighted_points(model, bone_indices[root_name])
        tip_points = _weighted_points(model, bone_indices[tip_name], threshold=0.25)
        root_center = _centroid(root_points)
        tip_center = _centroid(tip_points)
        root_radius = _radius(root_points, root_center, lower=0.52, upper=0.74)
        tip_radius = _radius(tip_points, tip_center, lower=0.14, upper=0.28)

        root_body_index = len(model.rigidbodies)
        model.rigidbodies.append(
            _make_body(
                pmx_struct,
                name_jp=f"{side}胸物理",
                name_en=f"Breast_{'L' if sign > 0 else 'R'}_Physics",
                bone_index=bone_indices[root_name],
                position=root_center,
                radius=root_radius,
                base_body=base_body,
                mass=root_mass,
                move_damping=root_move,
                rotation_damping=root_rotation,
            )
        )
        tip_body_index = len(model.rigidbodies)
        model.rigidbodies.append(
            _make_body(
                pmx_struct,
                name_jp=f"{side}胸先物理",
                name_en=f"Breast_{'L' if sign > 0 else 'R'}_Physics_Tip",
                bone_index=bone_indices[tip_name],
                position=tip_center,
                radius=tip_radius,
                base_body=base_body,
                mass=tip_mass,
                move_damping=tip_move,
                rotation_damping=tip_rotation,
            )
        )

        root_joint_index = len(model.joints)
        model.joints.append(
            _make_joint(
                pmx_struct,
                name_jp=f"{side}胸物理接続",
                name_en=f"Breast_{'L' if sign > 0 else 'R'}_Physics_Joint",
                first=base_body_index,
                second=root_body_index,
                position=model.bones[bone_indices[root_name]].pos,
                rotation_min=root_limit[0],
                rotation_max=root_limit[1],
                rotation_spring=root_spring,
            )
        )
        tip_joint_index = len(model.joints)
        model.joints.append(
            _make_joint(
                pmx_struct,
                name_jp=f"{side}胸先物理接続",
                name_en=f"Breast_{'L' if sign > 0 else 'R'}_Physics_Tip_Joint",
                first=root_body_index,
                second=tip_body_index,
                position=model.bones[bone_indices[tip_name]].pos,
                rotation_min=tip_limit[0],
                rotation_max=tip_limit[1],
                rotation_spring=tip_spring,
            )
        )
        created.append({
            "side": side,
            "rootBody": root_body_index,
            "tipBody": tip_body_index,
            "rootJoint": root_joint_index,
            "tipJoint": tip_joint_index,
            "rootRadius": root_radius,
            "tipRadius": tip_radius,
            "rootCenter": root_center,
            "tipCenter": tip_center,
        })

    return {
        "profile": profile,
        "baseChestBody": base_body_index,
        "bones": bone_indices,
        "created": created,
        "counts": {
            "vertices": len(model.verts),
            "materials": len(model.materials),
            "bones": len(model.bones),
            "morphs": len(model.morphs),
            "rigidbodies": len(model.rigidbodies),
            "joints": len(model.joints),
        },
    }


def _round_trip_report(pmx_parser, pmx_struct, path: Path, expected: dict) -> dict:
    model = _read_pmx(pmx_parser, path)
    names = {body.name_en for body in model.rigidbodies}
    expected_names = {
        f"Breast_{side}_Physics{suffix}"
        for side in ["L", "R"]
        for suffix in ["", "_Tip"]
    }
    missing = sorted(expected_names - names)
    if missing:
        raise ValueError(f"Round-trip PMX is missing added rigid bodies: {missing}")

    # The official Nuthouse01 structures validate all PMX sections before write.
    # Repeat the count and link checks after reading the actual output bytes.
    for joint in model.joints[-4:]:
        if not (0 <= joint.rb1_idx < len(model.rigidbodies)):
            raise ValueError("Round-trip joint A reference is invalid")
        if not (0 <= joint.rb2_idx < len(model.rigidbodies)):
            raise ValueError("Round-trip joint B reference is invalid")

    return {
        "outputSha256": _sha256(path),
        "outputBytes": path.stat().st_size,
        "counts": {
            "vertices": len(model.verts),
            "materials": len(model.materials),
            "bones": len(model.bones),
            "morphs": len(model.morphs),
            "rigidbodies": len(model.rigidbodies),
            "joints": len(model.joints),
        },
        "expectedAddedBodies": sorted(expected_names),
        "expectedProfile": expected["profile"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--toolchain-root", required=True, type=Path)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--profile", choices=["stable", "balanced", "max"], default="stable")
    args = parser.parse_args()

    input_path = args.input.resolve()
    output_path = args.output.resolve()
    if not input_path.is_file():
        raise FileNotFoundError(input_path)
    if output_path.exists():
        raise FileExistsError(f"Refusing to overwrite existing output: {output_path}")
    if input_path == output_path:
        raise ValueError("Input and output must be different files")

    pmx_parser, pmx_struct = _load_toolchain(args.toolchain_root.resolve())
    input_hash_before = _sha256(input_path)
    model = _read_pmx(pmx_parser, input_path)
    audit = _create_variant(model, pmx_struct, args.profile)
    _write_pmx(pmx_parser, output_path, model)
    round_trip = _round_trip_report(pmx_parser, pmx_struct, output_path, audit)
    input_hash_after = _sha256(input_path)
    if input_hash_before != input_hash_after:
        raise RuntimeError("The source PMX changed during variant creation")

    result = {
        "input": str(input_path),
        "output": str(output_path),
        "inputSha256": input_hash_after,
        "sourceUnchanged": input_hash_before == input_hash_after,
        "audit": audit,
        "roundTrip": round_trip,
    }
    print(json.dumps(result, ensure_ascii=True, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(json.dumps({"error": str(exc)}, ensure_ascii=True))
        raise
