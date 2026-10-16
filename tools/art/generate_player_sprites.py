#!/usr/bin/env python3
"""Aether — Rigged 2D Character Animation Generator & Model Sheet Builder.

Generates the complete production animation set (204 distinct, mathematically rigged frames),
contact sheets, model sheets, and packed runtime sprite sheets for the Aether protagonist.

Palette:
  Cloak: Midnight Navy (#142236) with Cyan trim (#288CA0)
  Armor: Silver-Steel Plate (#5A7288, #8CA4B8, #B2C8D8) with Dark Ink joints (#1A2430)
  Aether Core & Visor: Luminous Electric Cyan (#3AF0F8, #B0F8FC)
  Leather & Bronze: Weathered Straps (#3C2C20) and Gilded Fittings (#D8A438, #F0C458)
"""

from __future__ import annotations

import math
import os
import sys
from PIL import Image, ImageDraw

FRAME_SIZE = 96
GROUND_Y = 82
ROOT_X = 46

# Color Palette Constants (RGBA)
C_INK = (12, 16, 24, 255)
C_SHADOW = (10, 14, 20, 255)
C_NAVY_DEEP = (14, 22, 34, 255)
C_NAVY = (22, 36, 56, 255)
C_NAVY_LIGHT = (34, 54, 82, 255)
C_STEEL_DARK = (52, 68, 84, 255)
C_STEEL = (92, 118, 140, 255)
C_STEEL_LIGHT = (148, 178, 202, 255)
C_ICE = (212, 238, 255, 255)
C_GOLD_DARK = (160, 112, 32, 255)
C_GOLD = (226, 172, 54, 255)
C_GOLD_LIGHT = (252, 216, 108, 255)
C_CYAN_DIM = (28, 140, 160, 255)
C_CYAN = (58, 240, 248, 255)
C_CYAN_GLOW = (58, 240, 248, 120)
C_LEATHER_DARK = (38, 26, 18, 255)
C_LEATHER = (64, 46, 32, 255)
C_LEATHER_LIGHT = (98, 72, 52, 255)


def draw_capsule(draw: ImageDraw.ImageDraw, p1: tuple[float, float], p2: tuple[float, float],
                 r1: float, r2: float, fill: tuple, outline: tuple = C_INK):
    """Draws a tapered 2D limb segment between two points with radiuses r1 and r2."""
    dx = p2[0] - p1[0]
    dy = p2[1] - p1[1]
    length = math.hypot(dx, dy)
    if length < 0.001:
        draw.ellipse([p1[0] - r1, p1[1] - r1, p1[0] + r1, p1[1] + r1], fill=fill, outline=outline)
        return

    nx = -dy / length
    ny = dx / length

    # Outer outline polygon
    out_r1 = r1 + 1.0
    out_r2 = r2 + 1.0
    c1 = (p1[0] + nx * out_r1, p1[1] + ny * out_r1)
    c2 = (p2[0] + nx * out_r2, p2[1] + ny * out_r2)
    c3 = (p2[0] - nx * out_r2, p2[1] - ny * out_r2)
    c4 = (p1[0] - nx * out_r1, p1[1] - ny * out_r1)
    draw.polygon([c1, c2, c3, c4], fill=outline)
    draw.ellipse([p1[0] - out_r1, p1[1] - out_r1, p1[0] + out_r1, p1[1] + out_r1], fill=outline)
    draw.ellipse([p2[0] - out_r2, p2[1] - out_r2, p2[0] + out_r2, p2[1] + out_r2], fill=outline)

    # Inner fill polygon
    i1 = (p1[0] + nx * r1, p1[1] + ny * r1)
    i2 = (p2[0] + nx * r2, p2[1] + ny * r2)
    i3 = (p2[0] - nx * r2, p2[1] - ny * r2)
    i4 = (p1[0] - nx * r1, p1[1] - ny * r1)
    draw.polygon([i1, i2, i3, i4], fill=fill)
    draw.ellipse([p1[0] - r1, p1[1] - r1, p1[0] + r1, p1[1] + r1], fill=fill)
    draw.ellipse([p2[0] - r2, p2[1] - r2, p2[0] + r2, p2[1] + r2], fill=fill)


def render_character_pose(
    pelvis_y: float = 54.0,
    pelvis_x: float = ROOT_X,
    torso_lean: float = 0.0,      # degrees
    head_tilt: float = 0.0,       # degrees
    leg_l_hip: float = 0.0,       # degrees relative to down
    leg_l_knee: float = 0.0,
    leg_r_hip: float = 0.0,
    leg_r_knee: float = 0.0,
    arm_l_sh: float = 0.0,        # degrees
    arm_l_el: float = 0.0,
    arm_r_sh: float = 0.0,
    arm_r_el: float = 0.0,
    sword_angle: float = -42.0,   # degrees
    cloak_flow: float = 0.0,      # horizontal push for cloak
    cloak_lift: float = 0.0,      # vertical lift for cloak
    visor_intensity: float = 1.0,
    slash_progress: float = 0.0,  # 0: none, >0: draw crescent arc
    slash_radius: float = 28.0,
    slash_center_offset: tuple[float, float] = (16.0, -8.0),
    is_dead: bool = False
) -> Image.Image:
    """Renders one high-fidelity side-view character frame according to the skeletal rig."""
    img = Image.new("RGBA", (FRAME_SIZE, FRAME_SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    torso_rad = math.radians(torso_lean)
    head_rad = math.radians(torso_lean + head_tilt)

    # Torso & Neck anchors
    chest_x = pelvis_x + math.sin(torso_rad) * 14.0
    chest_y = pelvis_y - math.cos(torso_rad) * 14.0

    head_x = chest_x + math.sin(head_rad) * 10.0
    head_y = chest_y - math.cos(head_rad) * 10.0

    # -------------------------------------------------------------------------------------
    # 1. Back Arm (left) — in deeper shadow
    # -------------------------------------------------------------------------------------
    sh_l_x = chest_x - math.cos(torso_rad) * 3.5
    sh_l_y = chest_y - math.sin(torso_rad) * 3.5
    el_l_rad = math.radians(arm_l_sh)
    el_l_x = sh_l_x + math.sin(el_l_rad) * 10.0
    el_l_y = sh_l_y + math.cos(el_l_rad) * 10.0
    wr_l_rad = el_l_rad + math.radians(arm_l_el)
    wr_l_x = el_l_x + math.sin(wr_l_rad) * 9.0
    wr_l_y = el_l_y + math.cos(wr_l_rad) * 9.0
    draw_capsule(draw, (sh_l_x, sh_l_y), (el_l_x, el_l_y), 4.2, 3.6, C_STEEL_DARK)
    draw_capsule(draw, (el_l_x, el_l_y), (wr_l_x, wr_l_y), 3.4, 2.8, C_NAVY_DEEP)

    # -------------------------------------------------------------------------------------
    # 2. Back Leg (left) — in deeper shadow for depth
    # -------------------------------------------------------------------------------------
    hip_l_x = pelvis_x - 3.5
    hip_l_y = pelvis_y
    knee_l_rad = math.radians(leg_l_hip)
    knee_l_x = hip_l_x + math.sin(knee_l_rad) * 12.5
    knee_l_y = hip_l_y + math.cos(knee_l_rad) * 12.5
    ank_l_rad = knee_l_rad + math.radians(leg_l_knee)
    ank_l_x = knee_l_x + math.sin(ank_l_rad) * 12.5
    ank_l_y = knee_l_y + math.cos(ank_l_rad) * 12.5
    foot_l_y = min(GROUND_Y, ank_l_y)
    draw_capsule(draw, (hip_l_x, hip_l_y), (knee_l_x, knee_l_y), 4.8, 4.0, C_NAVY_DEEP)
    draw_capsule(draw, (knee_l_x, knee_l_y), (ank_l_x, foot_l_y), 4.0, 3.4, C_STEEL_DARK)
    # Back boot
    draw.polygon([(ank_l_x - 3, foot_l_y), (ank_l_x + 5, foot_l_y),
                  (ank_l_x + 4, foot_l_y - 4), (ank_l_x - 3, foot_l_y - 4)],
                 fill=C_LEATHER_DARK, outline=C_INK)

    # -------------------------------------------------------------------------------------
    # 3. Trailing Cloak (volumetric, multi-layered drape)
    # -------------------------------------------------------------------------------------
    c_root_x = chest_x - 4.5
    c_root_y = chest_y + 1.0
    c_tip1_x = c_root_x - 14.0 - cloak_flow * 0.85
    c_tip1_y = c_root_y + 24.0 - cloak_lift
    c_mid_x = c_root_x - 8.0 - cloak_flow * 0.5
    c_mid_y = c_root_y + 26.0 - cloak_lift * 0.7
    c_tip2_x = pelvis_x - 2.0
    c_tip2_y = pelvis_y + 6.0

    # Inner cloak shadow
    draw.polygon([(c_root_x, c_root_y), (c_tip1_x - 2, c_tip1_y),
                  (c_mid_x, c_mid_y), (c_tip2_x, c_tip2_y)],
                 fill=C_NAVY_DEEP, outline=C_INK)
    # Outer cloak body
    draw.polygon([(c_root_x, c_root_y), (c_tip1_x, c_tip1_y),
                  (c_mid_x + 1, c_mid_y), (c_tip2_x + 2, c_tip2_y)],
                 fill=C_NAVY, outline=C_INK)
    # Luminous cyan trim along hem
    draw.line([(c_tip1_x, c_tip1_y), (c_mid_x + 1, c_mid_y)], fill=C_CYAN, width=2)
    draw.line([(c_mid_x + 1, c_mid_y), (c_tip2_x + 2, c_tip2_y)], fill=C_CYAN_DIM, width=1)

    # -------------------------------------------------------------------------------------
    # 4. Front Leg (right) — heroic values with steel armor plates
    # -------------------------------------------------------------------------------------
    hip_r_x = pelvis_x + 3.0
    hip_r_y = pelvis_y
    knee_r_rad = math.radians(leg_r_hip)
    knee_r_x = hip_r_x + math.sin(knee_r_rad) * 12.5
    knee_r_y = hip_r_y + math.cos(knee_r_rad) * 12.5
    ank_r_rad = knee_r_rad + math.radians(leg_r_knee)
    ank_r_x = knee_r_x + math.sin(ank_r_rad) * 12.5
    ank_r_y = knee_r_y + math.cos(ank_r_rad) * 12.5
    foot_r_y = min(GROUND_Y, ank_r_y)

    # Thigh
    draw_capsule(draw, (hip_r_x, hip_r_y), (knee_r_x, knee_r_y), 5.4, 4.4, C_NAVY)
    # Shin / Greave
    draw_capsule(draw, (knee_r_x, knee_r_y), (ank_r_x, foot_r_y), 4.6, 3.8, C_STEEL)
    # Knee-cop armor plate & highlight
    draw.ellipse([knee_r_x - 3.5, knee_r_y - 3.5, knee_r_x + 3.5, knee_r_y + 3.5],
                 fill=C_STEEL_LIGHT, outline=C_INK)
    draw.line([(knee_r_x - 1, knee_r_y - 2), (knee_r_x + 2, knee_r_y - 1)], fill=C_ICE, width=1)

    # Front Boot with reinforced toe and leather cuff
    b_pts = [
        (ank_r_x - 3.5, foot_r_y - 6),
        (ank_r_x + 3.5, foot_r_y - 6),
        (ank_r_x + 6.5, foot_r_y),
        (ank_r_x - 4.0, foot_r_y)
    ]
    draw.polygon(b_pts, fill=C_LEATHER, outline=C_INK)
    # Sole & Steel Toe-tip
    draw.line([(ank_r_x - 4.0, foot_r_y), (ank_r_x + 6.5, foot_r_y)], fill=C_LEATHER_DARK, width=2)
    draw.polygon([(ank_r_x + 3.0, foot_r_y - 3), (ank_r_x + 6.5, foot_r_y),
                  (ank_r_x + 3.0, foot_r_y)], fill=C_STEEL_LIGHT, outline=C_INK)

    # -------------------------------------------------------------------------------------
    # 5. Torso / Cuirass (sculpted steel armor plate & gold aether crest)
    # -------------------------------------------------------------------------------------
    tx_min = chest_x - 6.5
    tx_max = chest_x + 6.5
    ty_min = chest_y - 3.0
    ty_max = pelvis_y + 3.0

    # Base tunic/under-armor
    draw.rounded_rectangle([tx_min - 1, ty_min, tx_max + 1, ty_max + 2], radius=4,
                           fill=C_NAVY_DEEP, outline=C_INK)
    # Steel Breastplate
    draw.rounded_rectangle([tx_min, ty_min, tx_max, ty_max - 2], radius=4,
                           fill=C_STEEL, outline=C_INK)
    # Light specular edge on right breastplate
    draw.line([(tx_max - 1, ty_min + 3), (tx_max - 1, ty_max - 4)], fill=C_STEEL_LIGHT, width=1)

    # Central Gilded Aether Core
    core_cx = (tx_min + tx_max) * 0.5
    core_cy = ty_min + 6.5
    draw.ellipse([core_cx - 3.5, core_cy - 3.5, core_cx + 3.5, core_cy + 3.5],
                 fill=C_GOLD, outline=C_INK)
    draw.ellipse([core_cx - 2.0, core_cy - 2.0, core_cx + 2.0, core_cy + 2.0],
                 fill=C_CYAN, outline=C_GOLD_DARK)
    draw.ellipse([core_cx - 0.8, core_cy - 0.8, core_cx + 0.8, core_cy + 0.8], fill=C_ICE)

    # Leather Waist Belt & Buckle
    belt_y1 = ty_max - 4.0
    belt_y2 = ty_max
    draw.rectangle([tx_min - 0.5, belt_y1, tx_max + 0.5, belt_y2], fill=C_LEATHER, outline=C_INK)
    draw.rectangle([core_cx - 2.5, belt_y1 - 0.5, core_cx + 2.5, belt_y2 + 0.5],
                   fill=C_GOLD, outline=C_INK)

    # -------------------------------------------------------------------------------------
    # 6. Head, Hooded Cowl & Luminous Aether Visor
    # -------------------------------------------------------------------------------------
    head_r = 9.2
    # Outer Hood silhouette
    draw.ellipse([head_x - head_r, head_y - head_r - 2, head_x + head_r, head_y + head_r],
                 fill=C_NAVY, outline=C_INK)
    # Peak of hood
    draw.polygon([(head_x - 3, head_y - head_r - 2),
                  (head_x + 5, head_y - head_r - 7),
                  (head_x + 1, head_y - head_r)],
                 fill=C_NAVY_LIGHT, outline=C_INK)
    # Winged Silver Crest
    draw.polygon([(head_x - 4, head_y - head_r + 1),
                  (head_x + 4, head_y - head_r - 5),
                  (head_x + 2, head_y - head_r + 1)],
                 fill=C_STEEL_LIGHT, outline=C_INK)
    # Brow cowl fold
    draw.ellipse([head_x - head_r + 2, head_y - 2, head_x + head_r - 1, head_y + head_r - 1],
                 fill=C_NAVY_LIGHT)

    # Deep shadow face cavity
    draw.ellipse([head_x - 4, head_y - 3.5, head_x + 6.5, head_y + 4.5], fill=C_INK)

    # Glowing Aether Visor (piercing horizontal slit)
    if not is_dead and visor_intensity > 0.04:
        v_alpha = int(255 * min(1.0, visor_intensity))
        # Cyan aura glow
        draw.line([(head_x - 1, head_y), (head_x + 6, head_y)],
                  fill=(58, 240, 248, v_alpha), width=2)
        # Intense white/ice energy core
        draw.line([(head_x + 2, head_y), (head_x + 5.5, head_y)],
                  fill=(216, 245, 255, v_alpha), width=1)

    # -------------------------------------------------------------------------------------
    # 7. Front Arm (right) & Shoulder Pauldron
    # -------------------------------------------------------------------------------------
    sh_r_x = chest_x + math.cos(torso_rad) * 4.2
    sh_r_y = chest_y + math.sin(torso_rad) * 2.5
    el_r_rad = math.radians(arm_r_sh)
    el_r_x = sh_r_x + math.sin(el_r_rad) * 10.5
    el_r_y = sh_r_y + math.cos(el_r_rad) * 10.5
    wr_r_rad = el_r_rad + math.radians(arm_r_el)
    wr_r_x = el_r_x + math.sin(wr_r_rad) * 9.5
    wr_r_y = el_r_y + math.cos(wr_r_rad) * 9.5

    # Flared Shoulder Pauldron
    draw.ellipse([sh_r_x - 4.5, sh_r_y - 4.5, sh_r_x + 4.5, sh_r_y + 4.5],
                 fill=C_STEEL_LIGHT, outline=C_INK)
    draw.arc([sh_r_x - 4.5, sh_r_y - 4.5, sh_r_x + 4.5, sh_r_y + 4.5],
             start=0, end=180, fill=C_GOLD, width=1)

    # Arm & Vambrace
    draw_capsule(draw, (sh_r_x, sh_r_y), (el_r_x, el_r_y), 4.8, 4.0, C_STEEL)
    draw_capsule(draw, (el_r_x, el_r_y), (wr_r_x, wr_r_y), 4.0, 3.2, C_STEEL_LIGHT)

    # Gauntlet Hand
    draw.ellipse([wr_r_x - 2.8, wr_r_y - 2.8, wr_r_x + 2.8, wr_r_y + 2.8],
                 fill=C_GOLD, outline=C_INK)

    # -------------------------------------------------------------------------------------
    # 8. The Aetherblade (Majestic Runic Melee Weapon)
    # -------------------------------------------------------------------------------------
    sw_rad = math.radians(sword_angle)
    blade_len = 26.0

    # Hilt grip & Pommel
    hilt_back_x = wr_r_x - math.cos(sw_rad) * 6.0
    hilt_back_y = wr_r_y - math.sin(sw_rad) * 6.0
    blade_tip_x = wr_r_x + math.cos(sw_rad) * blade_len
    blade_tip_y = wr_r_y + math.sin(sw_rad) * blade_len

    # Leather-wrapped Grip
    draw.line([(hilt_back_x, hilt_back_y), (wr_r_x, wr_r_y)], fill=C_LEATHER_DARK, width=3)
    # Gilded Pommel
    draw.ellipse([hilt_back_x - 2.0, hilt_back_y - 2.0, hilt_back_x + 2.0, hilt_back_y + 2.0],
                 fill=C_GOLD, outline=C_INK)

    # Flared Crossguard
    cg_x1 = wr_r_x + math.sin(sw_rad) * 5.0
    cg_y1 = wr_r_y - math.cos(sw_rad) * 5.0
    cg_x2 = wr_r_x - math.sin(sw_rad) * 5.0
    cg_y2 = wr_r_y + math.cos(sw_rad) * 5.0
    draw.line([(cg_x1, cg_y1), (cg_x2, cg_y2)], fill=C_INK, width=4)
    draw.line([(cg_x1, cg_y1), (cg_x2, cg_y2)], fill=C_GOLD, width=3)

    # Solid Tapered Blade Outline & Silver Body
    blade_pts = [
        (wr_r_x + math.sin(sw_rad) * 2.5, wr_r_y - math.cos(sw_rad) * 2.5),
        (blade_tip_x, blade_tip_y),
        (wr_r_x - math.sin(sw_rad) * 2.5, wr_r_y + math.cos(sw_rad) * 2.5)
    ]
    draw.polygon(blade_pts, fill=C_STEEL_LIGHT, outline=C_INK)
    # Silver Cutting Edge highlight
    draw.line([(wr_r_x + math.sin(sw_rad) * 2.0, wr_r_y - math.cos(sw_rad) * 2.0),
               (blade_tip_x, blade_tip_y)], fill=C_ICE, width=1)

    # Luminous Cyan Runic Fuller Channel
    f_start_x = wr_r_x + math.cos(sw_rad) * 3.0
    f_start_y = wr_r_y + math.sin(sw_rad) * 3.0
    f_end_x = blade_tip_x - math.cos(sw_rad) * 4.0
    f_end_y = blade_tip_y - math.sin(sw_rad) * 4.0
    draw.line([(f_start_x, f_start_y), (f_end_x, f_end_y)], fill=C_CYAN, width=2)
    draw.line([(f_start_x, f_start_y), (f_end_x, f_end_y)], fill=C_ICE, width=1)

    # -------------------------------------------------------------------------------------
    # 9. Attack Crescent Slash VFX (during active combat frames)
    # -------------------------------------------------------------------------------------
    if slash_progress > 0.0:
        arc_cx = chest_x + slash_center_offset[0]
        arc_cy = chest_y + slash_center_offset[1]
        r = slash_radius * min(1.0, slash_progress * 1.25)
        start_angle = -65.0
        end_angle = 70.0
        steps = 18
        prev_pt = None
        for i in range(steps + 1):
            t = i / float(steps)
            ang = math.radians(start_angle + (end_angle - start_angle) * t)
            px = arc_cx + math.cos(ang) * r
            py = arc_cy + math.sin(ang) * r
            if prev_pt:
                alpha = int(255 * (1.0 - abs(t - 0.5) * 1.5) * (1.0 - slash_progress * 0.35))
                col_main = (58, 240, 248, max(40, alpha))
                col_core = (220, 248, 255, max(80, int(alpha * 1.2)))
                draw.line([prev_pt, (px, py)], fill=col_main, width=4)
                draw.line([(prev_pt[0] + 0.5, prev_pt[1] + 0.5), (px + 0.5, py + 0.5)],
                          fill=col_core, width=2)
            prev_pt = (px, py)

    return img


# =========================================================================================
# Animation Sequence Authoring Functions
# =========================================================================================

def generate_idle_frames(count: int = 24) -> list[Image.Image]:
    """Generates 24-frame rhythmic breathing idle cycle."""
    frames = []
    for i in range(count):
        t = i / float(count)
        breath = math.sin(t * math.pi * 2.0)
        pelvis = 54.0 - breath * 1.4
        cloak_f = breath * 1.6
        visor_pulse = 0.9 + breath * 0.15
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=2.0 + breath * 1.2,
            head_tilt=-1.0 - breath * 0.8,
            leg_l_hip=-4.0, leg_l_knee=6.0,
            leg_r_hip=5.0, leg_r_knee=2.0,
            arm_l_sh=-15.0 - breath * 2.0, arm_l_el=25.0,
            arm_r_sh=10.0 + breath * 1.5, arm_r_el=35.0,
            sword_angle=-45.0 - breath * 2.0,
            cloak_flow=cloak_f,
            visor_intensity=visor_pulse
        )
        frames.append(img)
    return frames


def generate_idle_alt_frames(count: int = 24) -> list[Image.Image]:
    """Generates 24-frame idle variation: alert stance, visor flare, ready shift."""
    frames = []
    for i in range(count):
        t = i / float(count)
        phase = math.sin(t * math.pi * 2.0)
        turn_t = math.sin(t * math.pi)
        pelvis = 54.0 - phase * 0.8
        visor = 1.0 + turn_t * 0.6
        sword = -45.0 + turn_t * 22.0
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=4.0 + turn_t * 3.0,
            head_tilt=turn_t * 4.0,
            leg_l_hip=-6.0 + turn_t * 2.0, leg_l_knee=8.0,
            leg_r_hip=7.0 - turn_t * 2.0, leg_r_knee=4.0,
            arm_l_sh=-20.0 + turn_t * 10.0, arm_l_el=30.0,
            arm_r_sh=15.0 + turn_t * 20.0, arm_r_el=40.0,
            sword_angle=sword,
            cloak_flow=turn_t * 3.0,
            visor_intensity=visor
        )
        frames.append(img)
    return frames


def generate_walk_frames(count: int = 16) -> list[Image.Image]:
    """Generates 16-frame bipedal walk cycle."""
    frames = []
    for i in range(count):
        t = i / float(count)
        cycle = t * math.pi * 2.0
        bob = math.sin(cycle * 2.0)
        pelvis = 53.5 - abs(bob) * 1.8

        hip_l = math.sin(cycle) * 22.0
        knee_l = max(0.0, -math.sin(cycle) * 26.0)
        hip_r = math.sin(cycle + math.pi) * 22.0
        knee_r = max(0.0, -math.sin(cycle + math.pi) * 26.0)

        arm_l = math.sin(cycle + math.pi) * 25.0
        arm_r = math.sin(cycle) * 25.0

        cloak = math.sin(cycle) * 4.0 + 3.0
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=5.0,
            head_tilt=-2.0,
            leg_l_hip=hip_l, leg_l_knee=knee_l,
            leg_r_hip=hip_r, leg_r_knee=knee_r,
            arm_l_sh=arm_l, arm_l_el=25.0,
            arm_r_sh=arm_r, arm_r_el=30.0,
            sword_angle=-40.0 + arm_r * 0.4,
            cloak_flow=cloak
        )
        frames.append(img)
    return frames


def generate_run_frames(count: int = 20) -> list[Image.Image]:
    """Generates 20-frame athletic sprint cycle with weight transfer & flying cloak."""
    frames = []
    for i in range(count):
        t = i / float(count)
        cycle = t * math.pi * 2.0
        stride = math.sin(cycle)
        bob = math.cos(cycle * 2.0)
        pelvis = 55.0 - bob * 2.8

        hip_l = stride * 42.0
        knee_l = max(0.0, -stride * 48.0) if stride < 0 else 12.0
        hip_r = -stride * 42.0
        knee_r = max(0.0, stride * 48.0) if stride > 0 else 12.0

        arm_l = -stride * 45.0
        arm_r = stride * 45.0

        cloak_f = 14.0 + math.sin(cycle * 2.0) * 3.5
        cloak_l = 4.0 + bob * 2.0

        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=14.0,
            head_tilt=-6.0,
            leg_l_hip=hip_l, leg_l_knee=knee_l,
            leg_r_hip=hip_r, leg_r_knee=knee_r,
            arm_l_sh=arm_l - 10.0, arm_l_el=35.0,
            arm_r_sh=arm_r + 10.0, arm_r_el=40.0,
            sword_angle=-55.0 + arm_r * 0.3,
            cloak_flow=cloak_f,
            cloak_lift=cloak_l
        )
        frames.append(img)
    return frames


def generate_run_stop_frames(count: int = 8) -> list[Image.Image]:
    """Generates 8-frame deceleration skid and brake."""
    frames = []
    for i in range(count):
        t = i / float(count)
        pelvis = 56.0 - (1.0 - t) * 3.0
        lean = -8.0 * (1.0 - t) + 2.0 * t
        hip_r = 30.0 * (1.0 - t) + 5.0 * t
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=lean,
            head_tilt=4.0 * (1.0 - t),
            leg_l_hip=-15.0 * (1.0 - t), leg_l_knee=20.0,
            leg_r_hip=hip_r, leg_r_knee=10.0,
            arm_l_sh=-30.0 * (1.0 - t), arm_l_el=20.0,
            arm_r_sh=25.0 * (1.0 - t), arm_r_el=30.0,
            sword_angle=-30.0,
            cloak_flow=8.0 * (1.0 - t)
        )
        frames.append(img)
    return frames


def generate_jump_anticipation_frames(count: int = 6) -> list[Image.Image]:
    """Generates 6-frame pre-launch squat compression."""
    frames = []
    for i in range(count):
        t = i / float(count)
        pelvis = 54.0 + t * 5.0
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=8.0 * t,
            head_tilt=-4.0 * t,
            leg_l_hip=-12.0 * t, leg_l_knee=28.0 * t,
            leg_r_hip=12.0 * t, leg_r_knee=28.0 * t,
            arm_l_sh=-25.0 * t, arm_l_el=20.0,
            arm_r_sh=-20.0 * t, arm_r_el=30.0,
            sword_angle=-60.0,
            cloak_flow=2.0
        )
        frames.append(img)
    return frames


def generate_jump_rise_frames(count: int = 10) -> list[Image.Image]:
    """Generates 10-frame upward propulsion and rising arc."""
    frames = []
    for i in range(count):
        t = i / float(count)
        pelvis = 52.0 - t * 4.0
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=6.0,
            head_tilt=-4.0,
            leg_l_hip=-10.0, leg_l_knee=8.0,
            leg_r_hip=8.0, leg_r_knee=6.0,
            arm_l_sh=20.0, arm_l_el=15.0,
            arm_r_sh=35.0, arm_r_el=20.0,
            sword_angle=-20.0,
            cloak_flow=4.0,
            cloak_lift=-6.0
        )
        frames.append(img)
    return frames


def generate_jump_apex_frames(count: int = 8) -> list[Image.Image]:
    """Generates 8-frame hang-time crest at apex."""
    frames = []
    for i in range(count):
        t = i / float(count)
        pelvis = 48.0 + math.sin(t * math.pi) * 1.5
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=4.0,
            head_tilt=-2.0,
            leg_l_hip=-14.0, leg_l_knee=22.0,
            leg_r_hip=12.0, leg_r_knee=20.0,
            arm_l_sh=-10.0, arm_l_el=30.0,
            arm_r_sh=20.0, arm_r_el=35.0,
            sword_angle=-35.0,
            cloak_flow=8.0,
            cloak_lift=2.0
        )
        frames.append(img)
    return frames


def generate_jump_fall_frames(count: int = 10) -> list[Image.Image]:
    """Generates 10-frame downward descent with billowed cloak."""
    frames = []
    for i in range(count):
        t = i / float(count)
        pelvis = 50.0 + t * 4.0
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=8.0,
            head_tilt=3.0,
            leg_l_hip=-6.0, leg_l_knee=12.0,
            leg_r_hip=10.0, leg_r_knee=14.0,
            arm_l_sh=-30.0, arm_l_el=15.0,
            arm_r_sh=-15.0, arm_r_el=25.0,
            sword_angle=-50.0,
            cloak_flow=7.0,
            cloak_lift=8.0
        )
        frames.append(img)
    return frames


def generate_jump_land_frames(count: int = 8) -> list[Image.Image]:
    """Generates 8-frame landing compression and recovery."""
    frames = []
    for i in range(count):
        t = i / float(count)
        pelvis = 54.0 + (1.0 - t) * 5.0
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=10.0 * (1.0 - t),
            head_tilt=2.0 * (1.0 - t),
            leg_l_hip=-16.0 * (1.0 - t), leg_l_knee=32.0 * (1.0 - t),
            leg_r_hip=16.0 * (1.0 - t), leg_r_knee=32.0 * (1.0 - t),
            arm_l_sh=-20.0 * (1.0 - t), arm_l_el=20.0,
            arm_r_sh=10.0 * (1.0 - t), arm_r_el=30.0,
            sword_angle=-45.0,
            cloak_flow=3.0
        )
        frames.append(img)
    return frames


def generate_attack1_frames(count: int = 16) -> list[Image.Image]:
    """Generates 16-frame strike: windup (0-4), horizontal cleave (5-8), recovery (9-15)."""
    frames = []
    for i in range(count):
        if i < 5:
            p = i / 4.0
            pelvis = 54.0 - p * 1.5
            lean = -8.0 * p
            sword = -50.0 - p * 60.0
            arm_r = -40.0 * p
            slash = 0.0
        elif i < 9:
            p = (i - 5) / 3.0
            pelvis = 55.5 + p * 1.0
            lean = 16.0
            sword = -110.0 + p * 140.0
            arm_r = 45.0 + p * 20.0
            slash = 0.2 + p * 0.8
        else:
            p = (i - 9) / 6.0
            pelvis = 56.0 - p * 2.0
            lean = 14.0 * (1.0 - p)
            sword = 30.0 - p * 75.0
            arm_r = 60.0 - p * 45.0
            slash = max(0.0, 1.0 - p * 2.0)

        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=lean,
            leg_l_hip=-14.0, leg_l_knee=18.0,
            leg_r_hip=22.0, leg_r_knee=16.0,
            arm_l_sh=-25.0, arm_l_el=20.0,
            arm_r_sh=arm_r, arm_r_el=25.0,
            sword_angle=sword,
            cloak_flow=10.0,
            slash_progress=slash,
            slash_radius=30.0,
            slash_center_offset=(18.0, -4.0)
        )
        frames.append(img)
    return frames


def generate_attack2_frames(count: int = 16) -> list[Image.Image]:
    """Generates 16-frame combo finisher: rising diagonal crescent slash & flourish."""
    frames = []
    for i in range(count):
        if i < 5:
            p = i / 4.0
            pelvis = 56.0 + p * 2.0
            lean = 12.0
            sword = 20.0 + p * 40.0
            arm_r = 20.0 - p * 40.0
            slash = 0.0
        elif i < 9:
            p = (i - 5) / 3.0
            pelvis = 58.0 - p * 6.0
            lean = -10.0 + p * 14.0
            sword = 60.0 - p * 160.0
            arm_r = -20.0 + p * 80.0
            slash = 0.2 + p * 0.8
        else:
            p = (i - 9) / 6.0
            pelvis = 52.0 + p * 2.0
            lean = 4.0 * (1.0 - p)
            sword = -100.0 + p * 55.0
            arm_r = 60.0 - p * 45.0
            slash = max(0.0, 1.0 - p * 2.0)

        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=lean,
            leg_l_hip=-18.0, leg_l_knee=22.0,
            leg_r_hip=26.0, leg_r_knee=18.0,
            arm_l_sh=-30.0, arm_l_el=25.0,
            arm_r_sh=arm_r, arm_r_el=20.0,
            sword_angle=sword,
            cloak_flow=12.0,
            slash_progress=slash,
            slash_radius=34.0,
            slash_center_offset=(20.0, -12.0)
        )
        frames.append(img)
    return frames


def generate_dodge_frames(count: int = 12) -> list[Image.Image]:
    """Generates 12-frame horizontal phase dash with stream streaks."""
    frames = []
    for i in range(count):
        t = i / float(count)
        pelvis = 58.0 - math.sin(t * math.pi) * 2.0
        lean = 22.0
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=lean,
            head_tilt=-12.0,
            leg_l_hip=-35.0, leg_l_knee=15.0,
            leg_r_hip=35.0, leg_r_knee=20.0,
            arm_l_sh=-45.0, arm_l_el=10.0,
            arm_r_sh=45.0, arm_r_el=15.0,
            sword_angle=-65.0,
            cloak_flow=20.0,
            cloak_lift=-2.0,
            visor_intensity=1.5
        )
        frames.append(img)
    return frames


def generate_hurt_frames(count: int = 10) -> list[Image.Image]:
    """Generates 10-frame impact recoil and recovery."""
    frames = []
    for i in range(count):
        if i < 4:
            p = i / 3.0
            pelvis = 54.0 - p * 2.0
            lean = -18.0 * p
            head = 14.0 * p
            arm = -35.0 * p
        else:
            p = (i - 4) / 5.0
            pelvis = 52.0 + p * 2.0
            lean = -18.0 * (1.0 - p) + 2.0 * p
            head = 14.0 * (1.0 - p)
            arm = -35.0 * (1.0 - p)

        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=lean,
            head_tilt=head,
            leg_l_hip=-22.0 * (1.0 - p * 0.5), leg_l_knee=24.0,
            leg_r_hip=18.0 * (1.0 - p * 0.5), leg_r_knee=20.0,
            arm_l_sh=arm, arm_l_el=20.0,
            arm_r_sh=arm * 0.8, arm_r_el=25.0,
            sword_angle=-20.0 - lean,
            cloak_flow=-8.0 * (1.0 - p * 0.5)
        )
        frames.append(img)
    return frames


def generate_dead_frames(count: int = 16) -> list[Image.Image]:
    """Generates 16-frame collapse to ground and rest."""
    frames = []
    for i in range(count):
        t = min(1.0, i / 12.0)
        pelvis = 54.0 + t * 18.0
        lean = t * 45.0
        head = t * 30.0
        visor = max(0.0, 1.0 - t * 1.4)
        img = render_character_pose(
            pelvis_y=pelvis,
            torso_lean=lean,
            head_tilt=head,
            leg_l_hip=-30.0 * t, leg_l_knee=60.0 * t,
            leg_r_hip=-15.0 * t, leg_r_knee=50.0 * t,
            arm_l_sh=-20.0 + t * 40.0, arm_l_el=40.0,
            arm_r_sh=10.0 + t * 50.0, arm_r_el=50.0,
            sword_angle=10.0 + t * 70.0,
            cloak_flow=4.0,
            visor_intensity=visor,
            is_dead=(t >= 0.8)
        )
        frames.append(img)
    return frames


# =========================================================================================
# Model Sheet & Contact Sheets Generation
# =========================================================================================

def build_model_sheet(output_path: str):
    """Builds a comprehensive visual model sheet for the protagonist."""
    width = 1024
    height = 512
    img = Image.new("RGBA", (width, height), (16, 24, 36, 255))
    draw = ImageDraw.Draw(img)

    # Title & Headers
    draw.rectangle([0, 0, width, 48], fill=(12, 18, 28, 255))
    draw.line([(0, 48), (width, 48)], fill=C_CYAN, width=2)

    # Clean text rendering (pure ASCII without unicode box glyph issues)
    draw.text((24, 14), "AETHER - PROTAGONIST MODEL SHEET (AETHON / THE AETHER RANGER)", fill=(255, 255, 255, 255))
    draw.text((width - 240, 14), "PRODUCTION ASSET REF", fill=C_CYAN)

    # Turnaround Views
    views = [
        ("PROFILE (ACTION)", render_character_pose(pelvis_y=54.0, torso_lean=2.0)),
        ("GUARD STANCE", render_character_pose(pelvis_y=54.0, torso_lean=6.0, sword_angle=15.0, arm_r_sh=25.0)),
        ("COMBAT RUN", render_character_pose(pelvis_y=53.0, torso_lean=14.0, leg_l_hip=35.0, leg_r_hip=-35.0, cloak_flow=12.0)),
        ("CLEAVE ATTACK", render_character_pose(pelvis_y=55.0, torso_lean=16.0, sword_angle=40.0, slash_progress=0.8))
    ]

    for idx, (label, frame_img) in enumerate(views):
        bx = 40 + idx * 240
        by = 80
        # Background card
        draw.rounded_rectangle([bx, by, bx + 210, by + 280], radius=8, fill=(22, 34, 50, 255), outline=C_STEEL_DARK)
        # Scaled frame
        scaled = frame_img.resize((192, 192), Image.Resampling.NEAREST)
        img.paste(scaled, (bx + 9, by + 30), scaled)
        # Label
        draw.text((bx + 16, by + 245), label, fill=C_ICE)

    # Color Swatches Bar
    draw.rectangle([40, 390, width - 40, 480], fill=(22, 34, 50, 255), outline=C_STEEL_DARK)
    draw.text((56, 402), "PALETTE ASSIGNMENTS:", fill=C_ICE)

    swatches = [
        ("Midnight Navy", C_NAVY, "Cloak / Cowl"),
        ("Cyan Aether", C_CYAN, "Visor / Fuller"),
        ("Steel Armor", C_STEEL, "Cuirass / Greaves"),
        ("Silver Edge", C_ICE, "Blade Highlights"),
        ("Gilded Bronze", C_GOLD, "Crest Buckle"),
        ("Dark Ink", C_INK, "Joints / Outlines")
    ]

    for s_idx, (name, col, role) in enumerate(swatches):
        sx = 56 + s_idx * 155
        sy = 425
        draw.rectangle([sx, sy, sx + 24, sy + 24], fill=col, outline=C_INK)
        draw.text((sx + 32, sy + 2), name, fill=(255, 255, 255, 255))
        draw.text((sx + 32, sy + 14), role, fill=(160, 180, 200, 255))

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    img.save(output_path)
    print(f"  [model-sheet] Saved {output_path}")


def build_contact_sheet(frames: list[Image.Image], title: str, output_path: str, cols: int = 8):
    """Combines a list of animation frames into an annotated contact sheet."""
    rows = math.ceil(len(frames) / float(cols))
    pad = 8
    cell_w = FRAME_SIZE + pad * 2
    cell_h = FRAME_SIZE + pad * 2 + 16
    header_h = 40

    img = Image.new("RGBA", (cols * cell_w, rows * cell_h + header_h), (18, 26, 38, 255))
    draw = ImageDraw.Draw(img)

    # Header
    draw.rectangle([0, 0, img.width, header_h], fill=(12, 18, 26, 255))
    draw.line([(0, header_h), (img.width, header_h)], fill=C_CYAN, width=1)
    clean_title = title.replace("—", "-").replace("–", "-")
    draw.text((16, 12), f"{clean_title.upper()} - {len(frames)} FRAMES", fill=(255, 255, 255, 255))

    for idx, f in enumerate(frames):
        c = idx % cols
        r = idx // cols
        x = c * cell_w + pad
        y = r * cell_h + header_h + pad

        # Checkerboard background for transparency proof
        for cb_y in range(0, FRAME_SIZE, 12):
            for cb_x in range(0, FRAME_SIZE, 12):
                col = (28, 38, 52, 255) if ((cb_x // 12 + cb_y // 12) % 2 == 0) else (34, 46, 62, 255)
                draw.rectangle([x + cb_x, y + cb_y, x + cb_x + 12, y + cb_y + 12], fill=col)

        # Border
        draw.rectangle([x - 1, y - 1, x + FRAME_SIZE, y + FRAME_SIZE], outline=C_STEEL_DARK)
        # Paste frame
        img.paste(f, (x, y), f)
        # Label
        draw.text((x + 2, y + FRAME_SIZE + 2), f"#{idx:02d}", fill=(160, 190, 215, 255))

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    img.save(output_path)
    print(f"  [contact-sheet] Saved {output_path}")


# =========================================================================================
# Main Execution Entrypoint
# =========================================================================================

def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    art_dir = os.path.join(repo_root, "Assets", "Aether", "Art", "Characters", "Player")
    contact_dir = os.path.join(art_dir, "ContactSheets")
    frames_dir = os.path.join(art_dir, "Frames")
    res_dir = os.path.join(repo_root, "Assets", "Aether", "Resources", "Characters", "Player")

    os.makedirs(art_dir, exist_ok=True)
    os.makedirs(contact_dir, exist_ok=True)
    os.makedirs(frames_dir, exist_ok=True)
    os.makedirs(res_dir, exist_ok=True)

    print("Generating Aether Hero Animation Suites...")

    suites = [
        ("idle", "Hero Breathing Idle", generate_idle_frames(24)),
        ("idle_alt", "Hero Idle Alert Variation", generate_idle_alt_frames(24)),
        ("walk", "Hero Walk Cycle", generate_walk_frames(16)),
        ("run", "Hero Run Sprint Cycle", generate_run_frames(20)),
        ("run_stop", "Hero Run Deceleration Brake", generate_run_stop_frames(8)),
        ("jump_anticipation", "Hero Jump Anticipation", generate_jump_anticipation_frames(6)),
        ("jump_rise", "Hero Jump Upward Propulsion", generate_jump_rise_frames(10)),
        ("jump_apex", "Hero Jump Apex Crest", generate_jump_apex_frames(8)),
        ("jump_fall", "Hero Jump Downward Descent", generate_jump_fall_frames(10)),
        ("jump_land", "Hero Jump Landing Recovery", generate_jump_land_frames(8)),
        ("attack1", "Hero Horizontal Cleave Strike", generate_attack1_frames(16)),
        ("attack2", "Hero Rising Crescent Combo Finisher", generate_attack2_frames(16)),
        ("dodge", "Hero Phase Dash Dodge", generate_dodge_frames(12)),
        ("hurt", "Hero Impact Recoil Stagger", generate_hurt_frames(10)),
        ("dead", "Hero Defeat Collapse", generate_dead_frames(16)),
    ]

    all_frames: list[tuple[str, int, Image.Image]] = []
    total_frames = 0

    for slug, title, frames in suites:
        total_frames += len(frames)
        print(f"  Generated {title}: {len(frames)} frames")
        for idx, f in enumerate(frames):
            frame_path = os.path.join(frames_dir, f"hero_{slug}_{idx:02d}.png")
            f.save(frame_path)
            all_frames.append((slug, idx, f))

        contact_path = os.path.join(contact_dir, f"Hero_{slug.capitalize()}_ContactSheet.png")
        build_contact_sheet(frames, title, contact_path, cols=8)

    print(f"\nTotal Frames Rendered: {total_frames} across {len(suites)} distinct animations.")

    # Build Model Sheet
    model_sheet_path = os.path.join(art_dir, "Aether_Hero_ModelSheet.png")
    build_model_sheet(model_sheet_path)

    # Master Packed Sprite Sheet (16 columns x 13 rows)
    cols = 16
    rows = math.ceil(total_frames / float(cols))
    master_w = cols * FRAME_SIZE
    master_h = rows * FRAME_SIZE
    master_img = Image.new("RGBA", (master_w, master_h), (0, 0, 0, 0))

    for idx, (_slug, _frame_idx, f) in enumerate(all_frames):
        c = idx % cols
        r = idx // cols
        master_img.paste(f, (c * FRAME_SIZE, r * FRAME_SIZE))

    master_path = os.path.join(art_dir, "Aether_Hero_SpriteSheet.png")
    master_img.save(master_path)
    print(f"  [master-spritesheet] Saved {master_path} ({master_w}x{master_h})")

    # Save to Resources as well
    res_master_path = os.path.join(res_dir, "Aether_Hero_SpriteSheet.png")
    master_img.save(res_master_path)
    print(f"  [resources-spritesheet] Saved {res_master_path}")

    # Generate metadata catalog index file
    catalog_path = os.path.join(res_dir, "player_animations.json")
    import json
    catalog_data = {
        "frame_size": FRAME_SIZE,
        "total_frames": total_frames,
        "cols": cols,
        "rows": rows,
        "sequences": {}
    }
    cursor = 0
    for slug, title, frames in suites:
        catalog_data["sequences"][slug] = {
            "title": title,
            "start_frame": cursor,
            "frame_count": len(frames),
            "fps": 12 if "idle" in slug else 16 if "walk" in slug else 18 if "run" in slug else 20
        }
        cursor += len(frames)

    with open(catalog_path, "w", encoding="utf-8") as f:
        json.dump(catalog_data, f, indent=2)
    print(f"  [animation-catalog] Saved {catalog_path}")

    print("\nSprite Generation Complete.")


if __name__ == "__main__":
    main()
