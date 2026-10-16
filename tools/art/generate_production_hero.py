#!/usr/bin/env python3
"""
High-Resolution Hand-Painted 2D Protagonist Art & Animation Pipeline for Project Aether.

Replaces low-res 96x96 geometric pixel stickmen with authentic, hand-painted 256x256
character sprites sourced directly from the approved concept model sheet ("AETHER - The Last Ember").
Preserves original anatomy, painterly textures, specular highlights, cloth folds,
and crystalline Aetherblade across all 15 animation suites.
"""

import os
import math
import json
import numpy as np
from PIL import Image, ImageFilter, ImageDraw, ImageOps

FRAME_SIZE = 256
GROUND_Y = 240
CENTER_X = 128

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF_PATH = "/tmp/arena-coding-uploads/file_00000000610082108850da8d6e7353af.png"
ASSETS_DIR = os.path.join(REPO_ROOT, "tools", "art", "assets")


class HeroRig:
    """Manages segmented high-res hand-painted character layers and pose composition."""
    
    def __init__(self):
        hero_p = os.path.join(ASSETS_DIR, "hero_master.png")
        blade_p = os.path.join(ASSETS_DIR, "blade_master.png")
        cloak_p = os.path.join(ASSETS_DIR, "cloak_master.png")
        slash_p = os.path.join(ASSETS_DIR, "slash_master.png")
        
        self.hero_base = Image.open(hero_p).convert("RGBA")
        raw_blade = Image.open(blade_p).convert("RGBA")
        # Proportional blade length (~110px tall in-hand)
        self.blade = raw_blade.resize((int(raw_blade.width * 0.65), int(raw_blade.height * 0.65)), Image.Resampling.LANCZOS)
        self.cloak = Image.open(cloak_p).convert("RGBA")
        self.slash = Image.open(slash_p).convert("RGBA")

    def compose_frame(
        self,
        torso_dy: float = 0.0,
        torso_lean: float = 0.0,
        squash_y: float = 1.0,
        squash_x: float = 1.0,
        cloak_angle: float = 0.0,
        cloak_dx: float = 0.0,
        cloak_dy: float = 0.0,
        sword_visible: bool = False,
        sword_pos: tuple[float, float] = (145, 140),
        sword_angle: float = 0.0,
        slash_progress: float = 0.0,
        slash_type: int = 1, # 1: horizontal cleave, 2: rising crescent
        dodge_ghost: bool = False,
        hurt_flash: float = 0.0,
        defeat_t: float = 0.0
    ) -> Image.Image:
        """Composes a high-resolution 256x256 character frame."""
        frame = Image.new("RGBA", (FRAME_SIZE, FRAME_SIZE), (0, 0, 0, 0))
        
        # 1. Dodge motion blur ghosts
        if dodge_ghost:
            for offset_x, a_factor in [(-40, 0.25), (-20, 0.50)]:
                ghost = self.hero_base.copy()
                g_arr = np.array(ghost, dtype=np.float32)
                g_arr[:, :, 0] = np.clip(g_arr[:, :, 0] * 0.2 + 58 * 0.8, 0, 255)
                g_arr[:, :, 1] = np.clip(g_arr[:, :, 1] * 0.2 + 240 * 0.8, 0, 255)
                g_arr[:, :, 2] = np.clip(g_arr[:, :, 2] * 0.2 + 248 * 0.8, 0, 255)
                g_arr[:, :, 3] = g_arr[:, :, 3] * a_factor
                ghost_img = Image.fromarray(g_arr.astype(np.uint8))
                
                gx = CENTER_X - ghost_img.width // 2 + offset_x
                gy = GROUND_Y - ghost_img.height + int(torso_dy)
                frame.paste(ghost_img, (gx, gy), ghost_img)

        # 2. Billowing Cloak Layer (when dynamically streaming)
        if abs(cloak_angle) > 2.0 or abs(cloak_dx) > 2.0:
            rot_cloak = self.cloak.rotate(cloak_angle, resample=Image.Resampling.BICUBIC, expand=True)
            cx = CENTER_X - self.hero_base.width // 2 - 12 + int(cloak_dx)
            cy = GROUND_Y - self.hero_base.height + 25 + int(torso_dy) + int(cloak_dy)
            frame.paste(rot_cloak, (cx, cy), rot_cloak)

        # 3. Main Hand-Painted Hero Body
        body = self.hero_base.copy()
        
        # Squash & stretch
        if squash_y != 1.0 or squash_x != 1.0:
            new_w = max(10, int(body.width * squash_x))
            new_h = max(10, int(body.height * squash_y))
            body = body.resize((new_w, new_h), Image.Resampling.LANCZOS)
            
        # Torso lean / rotation
        if abs(torso_lean) > 0.5:
            body = body.rotate(torso_lean, resample=Image.Resampling.BICUBIC, expand=True)
            
        # Defeat collapse
        if defeat_t > 0.0:
            collapse_rot = defeat_t * 85.0
            body = body.rotate(collapse_rot, resample=Image.Resampling.BICUBIC, expand=True)
            fade = max(0.0, 1.0 - defeat_t * 0.75)
            b_arr = np.array(body, dtype=np.float32)
            b_arr[:, :, 3] *= fade
            body = Image.fromarray(b_arr.astype(np.uint8))
            
        # Hurt impact flash
        if hurt_flash > 0.0:
            b_arr = np.array(body, dtype=np.float32)
            b_arr[:, :, 0] = np.clip(b_arr[:, :, 0] * (1.0 - hurt_flash) + 255.0 * hurt_flash, 0, 255)
            b_arr[:, :, 1] = np.clip(b_arr[:, :, 1] * (1.0 - hurt_flash) + 120.0 * hurt_flash, 0, 255)
            b_arr[:, :, 2] = np.clip(b_arr[:, :, 2] * (1.0 - hurt_flash) + 120.0 * hurt_flash, 0, 255)
            body = Image.fromarray(b_arr.astype(np.uint8))

        bx = CENTER_X - body.width // 2 + int(torso_lean * 0.8)
        by = GROUND_Y - body.height + int(torso_dy)
        if defeat_t > 0:
            by += int(defeat_t * 36.0)
            
        frame.paste(body, (bx, by), body)

        # 4. Hand-Held Crystalline Aetherblade
        if sword_visible:
            rot_sword = self.blade.rotate(sword_angle, resample=Image.Resampling.BICUBIC, expand=True)
            sx = int(sword_pos[0]) - rot_sword.width // 2
            sy = int(sword_pos[1]) - rot_sword.height // 2
            frame.paste(rot_sword, (sx, sy), rot_sword)

        # 5. Hand-Painted Luminous Slash Effects
        if slash_progress > 0.0:
            scale_fac = 0.9 + slash_progress * 0.4
            cur_slash = self.slash.resize(
                (int(self.slash.width * scale_fac), int(self.slash.height * scale_fac)),
                Image.Resampling.BICUBIC
            )
            s_arr = np.array(cur_slash, dtype=np.float32)
            alpha_mult = min(1.0, slash_progress * 1.6) if slash_progress < 0.6 else max(0.0, (1.0 - slash_progress) * 2.2)
            s_arr[:, :, 3] = s_arr[:, :, 3] * alpha_mult
            faded_slash = Image.fromarray(s_arr.astype(np.uint8))
            
            if slash_type == 1:
                # Horizontal Cleave Crescent
                rot_slash = faded_slash.rotate(10 - (1.0 - slash_progress) * 20, resample=Image.Resampling.BICUBIC, expand=True)
                sx = sword_pos[0] - 25
                sy = sword_pos[1] - 85
            else:
                # Rising Crescent Finisher
                rot_slash = faded_slash.rotate(-30 + slash_progress * 35, resample=Image.Resampling.BICUBIC, expand=True)
                sx = sword_pos[0] - 15
                sy = sword_pos[1] - 95
                
            frame.paste(rot_slash, (int(sx), int(sy)), rot_slash)
            
        return frame


def generate_all_suites(rig: HeroRig) -> dict[str, tuple[str, list[Image.Image]]]:
    """Generates all 15 animation suites preserving hand-painted visual fidelity."""
    suites = {}
    
    # 1. Idle (24 frames, 12 FPS) - Gentle breathing and cloth wave
    idle_frames = []
    for i in range(24):
        phase = i / 24.0 * 2.0 * math.pi
        breath_y = math.sin(phase) * 1.5
        squash = 1.0 + math.sin(phase) * 0.012
        frame = rig.compose_frame(
            torso_dy=-breath_y,
            squash_y=squash
        )
        idle_frames.append(frame)
    suites["idle"] = ("Hero Breathing Idle", idle_frames)

    # 2. Idle Variation (24 frames, 12 FPS) - Alert stance with sword adjustment
    idle_alt_frames = []
    for i in range(24):
        t = i / 24.0
        phase = t * 2.0 * math.pi
        lean = math.sin(phase) * 1.5
        sword_ang = -10.0 + math.sin(phase) * 5.0
        frame = rig.compose_frame(
            torso_dy=-math.sin(phase) * 1.5,
            torso_lean=lean,
            sword_visible=(i >= 4 and i <= 20),
            sword_pos=(CENTER_X + 16, GROUND_Y - 95),
            sword_angle=sword_ang
        )
        idle_alt_frames.append(frame)
    suites["idle_alt"] = ("Hero Idle Alert Variation", idle_alt_frames)

    # 3. Walk Cycle (16 frames, 16 FPS) - Grounded tactical stride
    walk_frames = []
    for i in range(16):
        phase = i / 16.0 * 2.0 * math.pi
        step_bob = abs(math.sin(phase)) * 2.2
        lean = 1.5 + math.sin(phase) * 1.0
        clk_ang = -8.0 + math.sin(phase - 0.8) * 5.0
        frame = rig.compose_frame(
            torso_dy=-step_bob,
            torso_lean=-lean,
            cloak_angle=clk_ang,
            cloak_dx=-3.0
        )
        walk_frames.append(frame)
    suites["walk"] = ("Hero Walk Cycle", walk_frames)

    # 4. Run Sprint Cycle (20 frames, 18 FPS) - Forward sprint with billowing cape
    run_frames = []
    for i in range(20):
        phase = i / 20.0 * 2.0 * math.pi
        bounce = abs(math.sin(phase)) * 4.0
        lean = 10.0 + math.sin(phase) * 2.0
        clk_ang = -24.0 + math.sin(phase * 2.0) * 6.0
        frame = rig.compose_frame(
            torso_dy=-bounce + 2.0,
            torso_lean=-lean,
            cloak_angle=clk_ang,
            cloak_dx=-12.0,
            cloak_dy=-4.0
        )
        run_frames.append(frame)
    suites["run"] = ("Hero Run Sprint Cycle", run_frames)

    # 5. Run Stop / Deceleration (8 frames, 20 FPS)
    run_stop_frames = []
    for i in range(8):
        t = i / 7.0
        lean = (1.0 - t) * 10.0 - t * 3.0
        clk_ang = -20.0 * (1.0 - t) + 2.0 * t
        frame = rig.compose_frame(
            torso_dy=t * 2.0,
            torso_lean=-lean,
            cloak_angle=clk_ang,
            cloak_dx=-10.0 * (1.0 - t)
        )
        run_stop_frames.append(frame)
    suites["run_stop"] = ("Hero Run Deceleration Brake", run_stop_frames)

    # 6. Jump Anticipation (6 frames, 20 FPS) - Deep crouch
    jump_anticipation_frames = []
    for i in range(6):
        t = i / 5.0
        squash = 1.0 - t * 0.10
        frame = rig.compose_frame(
            torso_dy=t * 10.0,
            squash_y=squash,
            squash_x=1.0 + t * 0.04
        )
        jump_anticipation_frames.append(frame)
    suites["jump_anticipation"] = ("Hero Jump Anticipation", jump_anticipation_frames)

    # 7. Jump Rise (10 frames, 20 FPS) - Upward propulsion
    jump_rise_frames = []
    for i in range(10):
        t = i / 9.0
        rise_dy = -12.0 - t * 16.0
        stretch = 1.0 + (1.0 - t) * 0.06
        clk_ang = 6.0 + t * 8.0
        frame = rig.compose_frame(
            torso_dy=rise_dy,
            squash_y=stretch,
            squash_x=0.97,
            cloak_angle=clk_ang,
            cloak_dy=4.0
        )
        jump_rise_frames.append(frame)
    suites["jump_rise"] = ("Hero Jump Upward Propulsion", jump_rise_frames)

    # 8. Jump Apex (8 frames, 20 FPS) - Crest of arc
    jump_apex_frames = []
    for i in range(8):
        t = i / 7.0
        apex_dy = -30.0 + math.sin(t * math.pi) * 2.0
        frame = rig.compose_frame(
            torso_dy=apex_dy,
            cloak_angle=10.0 - t * 6.0,
            cloak_dy=3.0
        )
        jump_apex_frames.append(frame)
    suites["jump_apex"] = ("Hero Jump Apex Crest", jump_apex_frames)

    # 9. Jump Fall (10 frames, 20 FPS) - Downward aerodynamic dive
    jump_fall_frames = []
    for i in range(10):
        t = i / 9.0
        fall_dy = -26.0 + t * 22.0
        clk_ang = -14.0 - t * 6.0
        frame = rig.compose_frame(
            torso_dy=fall_dy,
            torso_lean=-3.0,
            cloak_angle=clk_ang,
            cloak_dy=-6.0
        )
        jump_fall_frames.append(frame)
    suites["jump_fall"] = ("Hero Jump Downward Descent", jump_fall_frames)

    # 10. Jump Land (8 frames, 20 FPS) - Impact compression
    jump_land_frames = []
    for i in range(8):
        t = i / 7.0
        comp = math.sin(t * math.pi) * 12.0
        squash = 1.0 - math.sin(t * math.pi) * 0.10
        frame = rig.compose_frame(
            torso_dy=comp,
            squash_y=squash,
            squash_x=1.0 + math.sin(t * math.pi) * 0.05
        )
        jump_land_frames.append(frame)
    suites["jump_land"] = ("Hero Jump Landing Recovery", jump_land_frames)

    # 11. Attack 1 - Horizontal Cleave (16 frames, 20 FPS)
    attack1_frames = []
    for i in range(16):
        if i < 5:
            # Startup windup
            t = i / 4.0
            sw_ang = 45.0 - t * 50.0
            sw_pos = (CENTER_X - 10 + t * 15, GROUND_Y - 110 + t * 10)
            frame = rig.compose_frame(
                torso_dy=t * 3.0,
                torso_lean=-t * 5.0,
                sword_visible=True,
                sword_pos=sw_pos,
                sword_angle=sw_ang
            )
        elif i < 9:
            # Active strike
            t = (i - 5) / 3.0
            sw_ang = -20.0 - t * 50.0
            sw_pos = (CENTER_X + 25 + t * 30, GROUND_Y - 95 - t * 10)
            frame = rig.compose_frame(
                torso_dy=3.0,
                torso_lean=-8.0,
                sword_visible=True,
                sword_pos=sw_pos,
                sword_angle=sw_ang,
                slash_progress=0.4 + t * 0.6,
                slash_type=1
            )
        else:
            # Recovery
            t = (i - 9) / 6.0
            sw_ang = -70.0 + t * 55.0
            sw_pos = (CENTER_X + 45 - t * 20, GROUND_Y - 100 + t * 15)
            frame = rig.compose_frame(
                torso_dy=3.0 * (1.0 - t),
                torso_lean=-8.0 * (1.0 - t),
                sword_visible=True,
                sword_pos=sw_pos,
                sword_angle=sw_ang,
                slash_progress=max(0.0, 1.0 - t * 1.5),
                slash_type=1
            )
        attack1_frames.append(frame)
    suites["attack1"] = ("Hero Horizontal Cleave Strike", attack1_frames)

    # 12. Attack 2 - Rising Crescent Finisher (16 frames, 20 FPS)
    attack2_frames = []
    for i in range(16):
        if i < 5:
            t = i / 4.0
            sw_ang = -55.0 + t * 25.0
            sw_pos = (CENTER_X - 5 + t * 10, GROUND_Y - 70 - t * 15)
            frame = rig.compose_frame(
                torso_dy=t * 4.0,
                torso_lean=t * 3.0,
                sword_visible=True,
                sword_pos=sw_pos,
                sword_angle=sw_ang
            )
        elif i < 9:
            t = (i - 5) / 3.0
            sw_ang = -30.0 + t * 90.0
            sw_pos = (CENTER_X + 15 + t * 20, GROUND_Y - 95 - t * 30)
            frame = rig.compose_frame(
                torso_dy=-t * 10.0,
                torso_lean=-5.0,
                sword_visible=True,
                sword_pos=sw_pos,
                sword_angle=sw_ang,
                slash_progress=0.4 + t * 0.6,
                slash_type=2
            )
        else:
            t = (i - 9) / 6.0
            sw_ang = 60.0 - t * 45.0
            sw_pos = (CENTER_X + 35 - t * 15, GROUND_Y - 125 + t * 30)
            frame = rig.compose_frame(
                torso_dy=-10.0 * (1.0 - t),
                torso_lean=-5.0 * (1.0 - t),
                sword_visible=True,
                sword_pos=sw_pos,
                sword_angle=sw_ang,
                slash_progress=max(0.0, 1.0 - t * 1.5),
                slash_type=2
            )
        attack2_frames.append(frame)
    suites["attack2"] = ("Hero Rising Crescent Combo Finisher", attack2_frames)

    # 13. Dodge Phase Dash (12 frames, 20 FPS)
    dodge_frames = []
    for i in range(12):
        t = i / 11.0
        lean = 16.0 * math.sin(t * math.pi)
        drop = math.sin(t * math.pi) * 6.0
        frame = rig.compose_frame(
            torso_dy=drop,
            torso_lean=-lean,
            cloak_angle=-28.0 * math.sin(t * math.pi),
            cloak_dx=-14.0,
            dodge_ghost=(i >= 2 and i <= 9)
        )
        dodge_frames.append(frame)
    suites["dodge"] = ("Hero Phase Dash Dodge", dodge_frames)

    # 14. Hurt Recoil (10 frames, 20 FPS)
    hurt_frames = []
    for i in range(10):
        t = i / 9.0
        knockback = math.sin(t * math.pi) * 12.0
        lean = -10.0 * math.sin(t * math.pi)
        flash = max(0.0, math.sin(t * math.pi) * 0.65)
        frame = rig.compose_frame(
            torso_dy=knockback * 0.3,
            torso_lean=lean,
            hurt_flash=flash
        )
        hurt_frames.append(frame)
    suites["hurt"] = ("Hero Impact Recoil Stagger", hurt_frames)

    # 15. Defeat Collapse (16 frames, 20 FPS)
    dead_frames = []
    for i in range(16):
        t = min(1.0, i / 12.0)
        frame = rig.compose_frame(
            defeat_t=t,
            cloak_angle=t * 12.0
        )
        dead_frames.append(frame)
    suites["dead"] = ("Hero Defeat Collapse", dead_frames)

    return suites


def build_contact_sheet(frames: list[Image.Image], title: str, output_path: str, cols: int = 8):
    """Combines animation frames into an annotated, transparency-proofed contact sheet."""
    rows = math.ceil(len(frames) / float(cols))
    pad = 8
    cell_w = FRAME_SIZE + pad * 2
    cell_h = FRAME_SIZE + pad * 2 + 18
    header_h = 44

    img = Image.new("RGBA", (cols * cell_w, rows * cell_h + header_h), (16, 24, 36, 255))
    draw = ImageDraw.Draw(img)

    # Header bar
    draw.rectangle([0, 0, img.width, header_h], fill=(10, 16, 24, 255))
    draw.line([(0, header_h), (img.width, header_h)], fill=(58, 240, 248, 255), width=2)
    clean_title = title.replace("—", "-").replace("–", "-")
    draw.text((20, 14), f"{clean_title.upper()} - {len(frames)} FRAMES (256x256 HIGH-RES)", fill=(255, 255, 255, 255))
    draw.text((img.width - 240, 14), "AUTHENTIC HAND-PAINTED 2D", fill=(58, 240, 248, 255))

    for idx, f in enumerate(frames):
        c = idx % cols
        r = idx // cols
        x = c * cell_w + pad
        y = r * cell_h + header_h + pad

        # Checkerboard background
        for cb_y in range(0, FRAME_SIZE, 16):
            for cb_x in range(0, FRAME_SIZE, 16):
                col = (26, 36, 50, 255) if ((cb_x // 16 + cb_y // 16) % 2 == 0) else (32, 44, 60, 255)
                draw.rectangle([x + cb_x, y + cb_y, x + cb_x + 16, y + cb_y + 16], fill=col)

        # Ground line indicator
        draw.line([(x, y + GROUND_Y), (x + FRAME_SIZE, y + GROUND_Y)], fill=(58, 240, 248, 100), width=1)
        # Outline
        draw.rectangle([x - 1, y - 1, x + FRAME_SIZE, y + FRAME_SIZE], outline=(92, 118, 140, 255))
        # Paste frame
        img.paste(f, (x, y), f)
        # Label
        draw.text((x + 4, y + FRAME_SIZE + 3), f"#{idx:02d}", fill=(180, 210, 235, 255))

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    img.save(output_path)
    print(f"  [contact-sheet] Saved {output_path}")


def build_model_sheet(ref_path: str, output_path: str):
    """Builds the authoritative model sheet matching the approved reference artwork."""
    ref = Image.open(ref_path).convert("RGBA")
    
    # Authentic model turnarounds and closeups
    model_section = ref.crop((440, 20, 1100, 600))
    closeups_section = ref.crop((920, 60, 1500, 520))
    
    out_w, out_h = 1440, 720
    sheet = Image.new("RGBA", (out_w, out_h), (12, 18, 28, 255))
    draw = ImageDraw.Draw(sheet)
    
    # Title Header
    draw.rectangle([0, 0, out_w, 54], fill=(8, 12, 18, 255))
    draw.line([(0, 54), (out_w, 54)], fill=(58, 240, 248, 255), width=2)
    draw.text((28, 16), "AETHER: THE LAST EMBER - PROTAGONIST MODEL SHEET (PRODUCTION MASTER)", fill=(255, 255, 255, 255))
    draw.text((out_w - 320, 16), "AUTHORITATIVE 2D ART REF", fill=(58, 240, 248, 255))
    
    sheet.paste(model_section, (30, 80))
    sheet.paste(closeups_section, (730, 80))
    
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    sheet.save(output_path)
    print(f"  [model-sheet] Saved {output_path}")


def main():
    print("Initializing High-Resolution Hand-Painted 2D Protagonist Art Pipeline...")
    
    rig = HeroRig()
    print("Rig instantiated from approved master concept artwork.")
    
    art_dir = os.path.join(REPO_ROOT, "Assets", "Aether", "Art", "Characters", "Player")
    contact_dir = os.path.join(art_dir, "ContactSheets")
    frames_dir = os.path.join(art_dir, "Frames")
    res_dir = os.path.join(REPO_ROOT, "Assets", "Aether", "Resources", "Characters", "Player")
    
    os.makedirs(art_dir, exist_ok=True)
    os.makedirs(contact_dir, exist_ok=True)
    os.makedirs(frames_dir, exist_ok=True)
    os.makedirs(res_dir, exist_ok=True)
    
    suites = generate_all_suites(rig)
    
    all_frames: list[tuple[str, int, Image.Image]] = []
    total_frames = 0
    
    for slug, (title, frames) in suites.items():
        total_frames += len(frames)
        print(f"  Rendered {title}: {len(frames)} frames @ 256x256")
        for idx, f in enumerate(frames):
            frame_path = os.path.join(frames_dir, f"hero_{slug}_{idx:02d}.png")
            f.save(frame_path)
            all_frames.append((slug, idx, f))
            
        contact_path = os.path.join(contact_dir, f"Hero_{slug.capitalize()}_ContactSheet.png")
        build_contact_sheet(frames, title, contact_path, cols=8)

    print(f"\nTotal Frames Rendered: {total_frames} across {len(suites)} distinct animations.")
    
    # Build Model Sheet
    model_sheet_path = os.path.join(art_dir, "Aether_Hero_ModelSheet.png")
    build_model_sheet(REF_PATH, model_sheet_path)
    
    # Master Packed Sprite Sheet:
    # 204 frames: 16 columns x 13 rows @ 256x256 = 4096 x 3328 pixels!
    cols = 16
    rows = math.ceil(total_frames / float(cols))
    master_w = cols * FRAME_SIZE
    master_h = rows * FRAME_SIZE
    
    print(f"\nPacking Master Sprite Sheet ({master_w}x{master_h})...")
    master_img = Image.new("RGBA", (master_w, master_h), (0, 0, 0, 0))
    for idx, (_slug, _frame_idx, f) in enumerate(all_frames):
        c = idx % cols
        r = idx // cols
        master_img.paste(f, (c * FRAME_SIZE, r * FRAME_SIZE))
        
    master_path = os.path.join(art_dir, "Aether_Hero_SpriteSheet.png")
    master_img.save(master_path)
    print(f"  [master-spritesheet] Saved {master_path}")
    
    res_master_path = os.path.join(res_dir, "Aether_Hero_SpriteSheet.png")
    master_img.save(res_master_path)
    print(f"  [resources-spritesheet] Saved {res_master_path}")
    
    # Update JSON Catalog
    catalog_path = os.path.join(res_dir, "player_animations.json")
    catalog_data = {
        "frame_size": FRAME_SIZE,
        "total_frames": total_frames,
        "cols": cols,
        "rows": rows,
        "sequences": {}
    }
    cursor = 0
    for slug, (title, frames) in suites.items():
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

    print("\nHigh-Resolution Protagonist Pipeline Execution Complete.")


if __name__ == "__main__":
    main()
