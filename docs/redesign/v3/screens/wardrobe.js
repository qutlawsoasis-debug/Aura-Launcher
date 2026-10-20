/**
 * Aura Launcher v3 — Wardrobe & Profile Screen Logic
 * Interactive 3D skin model renderer (Steve 4px / Alex 3px), cape rendering,
 * mouse drag inertia, quick angles, skin file validation and profile management.
 */

(function () {
  'use strict';

  // 1. Color Palettes for Coock0ld Founder Skin
  const SKIN_PALETTE = {
    // Face & Skin
    skin: '#d79e78',
    skinShadow: '#b87c56',
    hair: '#1d1510',
    hairHighlight: '#2c1e17',
    eyes: '#ffffff',
    pupil: '#2c3580',
    mouth: '#783824',

    // Founder Jacket
    jacketDark: '#0e1722',
    jacketMid: '#162334',
    jacketLight: '#1f3148',
    amberTrim: '#f2a63c',
    amberGlow: '#f7b44f',
    amberBelt: '#d98b25',

    // Pants & Boots
    pantsDark: '#12161f',
    pantsMid: '#1a202d',
    boots: '#090c12'
  };

  // State
  let modelType = 'classic'; // 'classic' (4px) or 'slim' (3px)
  let currentCape = 'founder'; // 'founder', 'classic', 'none'
  let autoRotate = true;
  let rotationY = 0; // in degrees
  let rotationX = 4; // in degrees (subtle pitch)
  let targetRotationY = null;
  let isDragging = false;
  let lastPointerX = 0;
  let lastPointerY = 0;
  let dragVelocity = 0;
  let customSkinLoaded = false;
  let customSkinImage = null;

  // 2. 3D Renderer Setup
  const canvas = document.getElementById('skinCanvas3D');
  const ctx = canvas ? canvas.getContext('2d') : null;
  const viewport = document.getElementById('skinViewportWrap');
  const hudAngle = document.getElementById('hudAngleBadge');

  // Math helpers
  const DEG_TO_RAD = Math.PI / 180;

  function rotateY(x, y, z, angleRad) {
    const cos = Math.cos(angleRad);
    const sin = Math.sin(angleRad);
    return {
      x: x * cos + z * sin,
      y: y,
      z: -x * sin + z * cos
    };
  }

  function rotateX(x, y, z, angleRad) {
    const cos = Math.cos(angleRad);
    const sin = Math.sin(angleRad);
    return {
      x: x,
      y: y * cos - z * sin,
      z: y * sin + z * cos
    };
  }

  function project(p, width, height, scale, fov) {
    const cameraDist = 80;
    const factor = fov / (cameraDist + p.z);
    return {
      x: width / 2 + p.x * scale * factor,
      y: height / 2 + p.y * scale * factor,
      z: p.z
    };
  }

  // Draw a 3D Quad
  function drawQuad(pts, color, strokeColor) {
    if (!ctx) return;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    ctx.lineTo(pts[1].x, pts[1].y);
    ctx.lineTo(pts[2].x, pts[2].y);
    ctx.lineTo(pts[3].x, pts[3].y);
    ctx.closePath();

    ctx.fillStyle = color;
    ctx.fill();

    if (strokeColor) {
      ctx.strokeStyle = strokeColor;
      ctx.lineWidth = 0.8;
      ctx.stroke();
    }
  }

  // Generate a Cuboid mesh
  function makeCuboid(cx, cy, cz, w, h, d, colors) {
    const hw = w / 2;
    const hh = h / 2;
    const hd = d / 2;

    const vertices = [
      { x: cx - hw, y: cy - hh, z: cz - hd }, // 0
      { x: cx + hw, y: cy - hh, z: cz - hd }, // 1
      { x: cx + hw, y: cy + hh, z: cz - hd }, // 2
      { x: cx - hw, y: cy + hh, z: cz - hd }, // 3
      { x: cx - hw, y: cy - hh, z: cz + hd }, // 4
      { x: cx + hw, y: cy - hh, z: cz + hd }, // 5
      { x: cx + hw, y: cy + hh, z: cz + hd }, // 6
      { x: cx - hw, y: cy + hh, z: cz + hd }  // 7
    ];

    // 6 faces: [v0, v1, v2, v3, normal, color]
    return [
      { v: [4, 5, 6, 7], color: colors.front, normal: { x: 0, y: 0, z: 1 } },   // Front
      { v: [1, 0, 3, 2], color: colors.back, normal: { x: 0, y: 0, z: -1 } },   // Back
      { v: [0, 4, 7, 3], color: colors.left, normal: { x: -1, y: 0, z: 0 } },   // Left
      { v: [5, 1, 2, 6], color: colors.right, normal: { x: 1, y: 0, z: 0 } },   // Right
      { v: [4, 5, 1, 0], color: colors.top, normal: { x: 0, y: -1, z: 0 } },    // Top
      { v: [7, 6, 2, 3], color: colors.bottom, normal: { x: 0, y: 1, z: 0 } }   // Bottom
    ];
  }

  // Generate Cape Mesh
  function makeCape(cx, cy, cz, w, h, d, capeType) {
    if (capeType === 'none') return [];

    const angle = 15 * DEG_TO_RAD; // Flap angle
    const hw = w / 2;
    const hd = d / 2;

    const topZ = cz;
    const botZ = cz - Math.sin(angle) * h;
    const botY = cy + Math.cos(angle) * h;

    const v = [
      { x: cx - hw, y: cy, z: topZ - hd },
      { x: cx + hw, y: cy, z: topZ - hd },
      { x: cx + hw, y: botY, z: botZ - hd },
      { x: cx - hw, y: botY, z: botZ - hd },
      { x: cx - hw, y: cy, z: topZ + hd },
      { x: cx + hw, y: cy, z: topZ + hd },
      { x: cx + hw, y: botY, z: botZ + hd },
      { x: cx - hw, y: botY, z: botZ + hd }
    ];

    const isFounder = capeType === 'founder';
    const backColor = isFounder ? '#0d1017' : '#0e1520';
    const frontColor = isFounder ? '#161a24' : '#1c2738';
    const trimColor = isFounder ? '#f2a63c' : '#b0bec5';

    return [
      { v: [1, 0, 3, 2], color: backColor, normal: { x: 0, y: 0, z: -1 }, isCapeBack: true, isFounder: isFounder },
      { v: [4, 5, 6, 7], color: frontColor, normal: { x: 0, y: 0, z: 1 } },
      { v: [0, 4, 7, 3], color: trimColor, normal: { x: -1, y: 0, z: 0 } },
      { v: [5, 1, 2, 6], color: trimColor, normal: { x: 1, y: 0, z: 0 } },
      { v: [4, 5, 1, 0], color: trimColor, normal: { x: 0, y: -1, z: 0 } },
      { v: [7, 6, 2, 3], color: trimColor, normal: { x: 0, y: 1, z: 0 } }
    ];
  }

  // Draw Cape Gold Crest on Back Face
  function drawCapeEmblem(pts, isFounder) {
    if (!ctx) return;
    const midX = (pts[0].x + pts[1].x + pts[2].x + pts[3].x) / 4;
    const midY = (pts[0].y + pts[1].y + pts[2].y + pts[3].y) / 4;
    const w = Math.hypot(pts[1].x - pts[0].x, pts[1].y - pts[0].y);

    if (w < 20) return;

    ctx.save();
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = `bold ${Math.floor(w * 0.42)}px Manrope, sans-serif`;

    if (isFounder) {
      ctx.fillStyle = '#f2a63c';
      ctx.shadowColor = 'rgba(242, 166, 60, 0.65)';
      ctx.shadowBlur = 8;
      ctx.fillText('A', midX, midY - 6);
    } else {
      ctx.fillStyle = '#cfd8dc';
      ctx.shadowColor = 'rgba(255, 255, 255, 0.4)';
      ctx.shadowBlur = 6;
      ctx.fillText('M', midX, midY - 6);
    }
    ctx.restore();
  }

  // Draw Character Head Details (Eyes, Hair, Beard)
  function drawHeadFace(pts) {
    if (!ctx) return;
    const w = Math.hypot(pts[1].x - pts[0].x, pts[1].y - pts[0].y);
    const h = Math.hypot(pts[3].x - pts[0].x, pts[3].y - pts[0].y);

    if (w < 20) return;

    // Hair Top Strip
    ctx.save();
    ctx.fillStyle = SKIN_PALETTE.hair;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    ctx.lineTo(pts[1].x, pts[1].y);
    ctx.lineTo(pts[1].x + (pts[2].x - pts[1].x) * 0.28, pts[1].y + (pts[2].y - pts[1].y) * 0.28);
    ctx.lineTo(pts[0].x + (pts[3].x - pts[0].x) * 0.28, pts[0].y + (pts[3].y - pts[0].y) * 0.28);
    ctx.closePath();
    ctx.fill();

    // Eyes
    const eyeY = 0.46;
    const eyeH = 0.12;

    // Left Eye
    const e1X1 = pts[0].x + (pts[1].x - pts[0].x) * 0.16 + (pts[3].x - pts[0].x) * eyeY;
    const e1Y1 = pts[0].y + (pts[1].y - pts[0].y) * 0.16 + (pts[3].y - pts[0].y) * eyeY;
    const e1W = (pts[1].x - pts[0].x) * 0.22;
    const e1H = (pts[3].y - pts[0].y) * eyeH;

    ctx.fillStyle = '#ffffff';
    ctx.fillRect(e1X1, e1Y1, e1W, e1H);
    ctx.fillStyle = SKIN_PALETTE.pupil;
    ctx.fillRect(e1X1 + e1W * 0.5, e1Y1, e1W * 0.5, e1H);

    // Right Eye
    const e2X1 = pts[0].x + (pts[1].x - pts[0].x) * 0.62 + (pts[3].x - pts[0].x) * eyeY;
    const e2Y1 = pts[0].y + (pts[1].y - pts[0].y) * 0.62 + (pts[3].y - pts[0].y) * eyeY;
    const e2W = (pts[1].x - pts[0].x) * 0.22;
    const e2H = (pts[3].y - pts[0].y) * eyeH;

    ctx.fillStyle = '#ffffff';
    ctx.fillRect(e2X1, e2Y1, e2W, e2H);
    ctx.fillStyle = SKIN_PALETTE.pupil;
    ctx.fillRect(e2X1, e2Y1, e2W * 0.5, e2H);

    // Nose & Mouth
    const noseX = pts[0].x + (pts[1].x - pts[0].x) * 0.38 + (pts[3].x - pts[0].x) * 0.64;
    const noseY = pts[0].y + (pts[1].y - pts[0].y) * 0.38 + (pts[3].y - pts[0].y) * 0.64;
    ctx.fillStyle = SKIN_PALETTE.mouth;
    ctx.fillRect(noseX, noseY, (pts[1].x - pts[0].x) * 0.24, (pts[3].y - pts[0].y) * 0.12);

    ctx.restore();
  }

  // Draw Amber Founder Trim on Jacket Front
  function drawJacketTrim(pts) {
    if (!ctx) return;
    const w = Math.hypot(pts[1].x - pts[0].x, pts[1].y - pts[0].y);
    if (w < 20) return;

    ctx.save();
    // Center amber zipper line
    const midTopX = (pts[0].x + pts[1].x) / 2;
    const midTopY = (pts[0].y + pts[1].y) / 2;
    const midBotX = (pts[3].x + pts[2].x) / 2;
    const midBotY = (pts[3].y + pts[2].y) / 2;

    ctx.strokeStyle = '#f2a63c';
    ctx.lineWidth = 2.2;
    ctx.beginPath();
    ctx.moveTo(midTopX, midTopY);
    ctx.lineTo(midBotX, midBotY);
    ctx.stroke();

    // Collar trim
    ctx.fillStyle = '#f2a63c';
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    ctx.lineTo(midTopX, midTopY + 12);
    ctx.lineTo(pts[1].x, pts[1].y);
    ctx.stroke();

    ctx.restore();
  }

  // Render Full 3D Character Scene
  function render3DScene() {
    if (!ctx || !canvas) return;

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    const radY = rotationY * DEG_TO_RAD;
    const radX = rotationX * DEG_TO_RAD;
    const scale = 5.2;
    const fov = 42;

    const armWidth = modelType === 'slim' ? 3.0 : 4.0;
    const armOffset = 4.0 + armWidth / 2;

    // Collect all parts
    const parts = [];

    // 1. Head (8x8x8)
    parts.push({
      type: 'head',
      faces: makeCuboid(0, -22, 0, 8, 8, 8, {
        front: SKIN_PALETTE.skin,
        back: SKIN_PALETTE.hair,
        left: SKIN_PALETTE.hair,
        right: SKIN_PALETTE.hair,
        top: SKIN_PALETTE.hair,
        bottom: SKIN_PALETTE.skinShadow
      })
    });

    // 2. Torso (8x12x4)
    parts.push({
      type: 'torso',
      faces: makeCuboid(0, -2, 0, 8, 12, 4, {
        front: SKIN_PALETTE.jacketMid,
        back: SKIN_PALETTE.jacketDark,
        left: SKIN_PALETTE.jacketDark,
        right: SKIN_PALETTE.jacketDark,
        top: SKIN_PALETTE.jacketLight,
        bottom: SKIN_PALETTE.pantsDark
      })
    });

    // 3. Right Arm (armWidth x 12 x 4)
    parts.push({
      type: 'rightArm',
      faces: makeCuboid(armOffset, -2, 0, armWidth, 12, 4, {
        front: SKIN_PALETTE.jacketMid,
        back: SKIN_PALETTE.jacketDark,
        left: SKIN_PALETTE.jacketMid,
        right: SKIN_PALETTE.skin,
        top: SKIN_PALETTE.jacketLight,
        bottom: SKIN_PALETTE.skinShadow
      })
    });

    // 4. Left Arm (armWidth x 12 x 4)
    parts.push({
      type: 'leftArm',
      faces: makeCuboid(-armOffset, -2, 0, armWidth, 12, 4, {
        front: SKIN_PALETTE.jacketMid,
        back: SKIN_PALETTE.jacketDark,
        left: SKIN_PALETTE.skin,
        right: SKIN_PALETTE.jacketMid,
        top: SKIN_PALETTE.jacketLight,
        bottom: SKIN_PALETTE.skinShadow
      })
    });

    // 5. Right Leg (4x12x4)
    parts.push({
      type: 'rightLeg',
      faces: makeCuboid(2, 22, 0, 4, 12, 4, {
        front: SKIN_PALETTE.pantsMid,
        back: SKIN_PALETTE.pantsDark,
        left: SKIN_PALETTE.pantsMid,
        right: SKIN_PALETTE.pantsDark,
        top: SKIN_PALETTE.pantsDark,
        bottom: SKIN_PALETTE.boots
      })
    });

    // 6. Left Leg (4x12x4)
    parts.push({
      type: 'leftLeg',
      faces: makeCuboid(-2, 22, 0, 4, 12, 4, {
        front: SKIN_PALETTE.pantsMid,
        back: SKIN_PALETTE.pantsDark,
        left: SKIN_PALETTE.pantsDark,
        right: SKIN_PALETTE.pantsMid,
        top: SKIN_PALETTE.pantsDark,
        bottom: SKIN_PALETTE.boots
      })
    });

    // 7. Cape (10x16x1)
    if (currentCape !== 'none') {
      parts.push({
        type: 'cape',
        faces: makeCape(0, -8, -2.5, 9.6, 15.5, 0.8, currentCape)
      });
    }

    // Flatten, transform and sort faces by depth (Painter's algorithm)
    const renderQueue = [];

    parts.forEach(part => {
      part.faces.forEach(face => {
        // Transform vertices
        const transformedVertices = face.v.map(vIdx => {
          // Cuboid standard vertices indexing lookup
          const hw = (part.type === 'leftArm' || part.type === 'rightArm') ? armWidth / 2 :
                     (part.type === 'torso') ? 4 :
                     (part.type === 'cape') ? 4.8 :
                     (part.type === 'head') ? 4 : 2;
          const hh = (part.type === 'head') ? 4 : (part.type === 'cape' ? 7.75 : 6);
          const hd = (part.type === 'head') ? 4 : (part.type === 'cape' ? 0.4 : 2);

          let cx = 0, cy = 0, cz = 0;
          if (part.type === 'head') cy = -22;
          else if (part.type === 'torso') cy = -2;
          else if (part.type === 'rightArm') { cx = armOffset; cy = -2; }
          else if (part.type === 'leftArm') { cx = -armOffset; cy = -2; }
          else if (part.type === 'rightLeg') { cx = 2; cy = 22; }
          else if (part.type === 'leftLeg') { cx = -2; cy = 22; }
          else if (part.type === 'cape') { cx = 0; cy = -8; cz = -2.5; }

          let x = cx, y = cy, z = cz;
          if (part.type === 'cape') {
            // Cape specific angled coordinates
            const angle = 15 * DEG_TO_RAD;
            const topZ = cz;
            const botZ = cz - Math.sin(angle) * 15.5;
            const botY = cy + Math.cos(angle) * 15.5;

            const capeCoords = [
              { x: -hw, y: cy, z: topZ - hd },
              { x: hw, y: cy, z: topZ - hd },
              { x: hw, y: botY, z: botZ - hd },
              { x: -hw, y: botY, z: botZ - hd },
              { x: -hw, y: cy, z: topZ + hd },
              { x: hw, y: cy, z: topZ + hd },
              { x: hw, y: botY, z: botZ + hd },
              { x: -hw, y: botY, z: botZ + hd }
            ];
            x = capeCoords[vIdx].x;
            y = capeCoords[vIdx].y;
            z = capeCoords[vIdx].z;
          } else {
            const xSign = (vIdx === 1 || vIdx === 2 || vIdx === 5 || vIdx === 6) ? 1 : -1;
            const ySign = (vIdx === 2 || vIdx === 3 || vIdx === 6 || vIdx === 7) ? 1 : -1;
            const zSign = (vIdx >= 4) ? 1 : -1;

            x = cx + xSign * hw;
            y = cy + ySign * hh;
            z = cz + zSign * hd;
          }

          // Rotate around Y and X
          const r1 = rotateY(x, y, z, radY);
          const r2 = rotateX(r1.x, r1.y, r1.z, radX);
          return r2;
        });

        // Compute Face Normal in world space
        const n1 = rotateY(face.normal.x, face.normal.y, face.normal.z, radY);
        const n2 = rotateX(n1.x, n1.y, n1.z, radX);

        // Back-face culling: skip if facing away from camera (Z <= 0)
        if (n2.z <= -0.05) return;

        // Project to 2D
        const projectedPts = transformedVertices.map(v => project(v, canvas.width, canvas.height, scale, fov));

        // Average depth for sorting
        const avgZ = (transformedVertices[0].z + transformedVertices[1].z + transformedVertices[2].z + transformedVertices[3].z) / 4;

        // Subtle ambient directional lighting based on normal
        const lightDir = { x: 0.3, y: -0.6, z: 0.7 };
        const dot = n2.x * lightDir.x + n2.y * lightDir.y + n2.z * lightDir.z;
        const brightness = Math.max(0.72, Math.min(1.18, 0.95 + dot * 0.28));

        renderQueue.push({
          pts: projectedPts,
          color: face.color,
          brightness: brightness,
          avgZ: avgZ,
          partType: part.type,
          isCapeBack: face.isCapeBack,
          isFounder: face.isFounder,
          isFrontFace: face.normal.z === 1
        });
      });
    });

    // Sort by depth (farthest first)
    renderQueue.sort((a, b) => a.avgZ - b.avgZ);

    // Draw sorted faces
    renderQueue.forEach(item => {
      // Adjust color with lighting
      let faceColor = item.color;
      if (item.brightness !== 1.0) {
        ctx.save();
        drawQuad(item.pts, faceColor, 'rgba(0, 0, 0, 0.15)');
        if (item.brightness < 1.0) {
          ctx.fillStyle = `rgba(0, 0, 0, ${((1.0 - item.brightness) * 0.55).toFixed(2)})`;
          ctx.fill();
        } else {
          ctx.fillStyle = `rgba(255, 255, 255, ${((item.brightness - 1.0) * 0.22).toFixed(2)})`;
          ctx.fill();
        }
        ctx.restore();
      } else {
        drawQuad(item.pts, faceColor, 'rgba(0, 0, 0, 0.15)');
      }

      // Overlays
      if (item.partType === 'head' && item.isFrontFace) {
        drawHeadFace(item.pts);
      } else if (item.partType === 'torso' && item.isFrontFace) {
        drawJacketTrim(item.pts);
      } else if (item.isCapeBack) {
        drawCapeEmblem(item.pts, item.isFounder);
      }
    });

    // Update HUD angle
    if (hudAngle) {
      const normalizedAngle = Math.round(((rotationY % 360) + 360) % 360);
      hudAngle.textContent = `${normalizedAngle}°`;
    }
  }

  // Animation Loop
  function tick() {
    // Smooth angle transition if target angle set
    if (targetRotationY !== null) {
      const diff = targetRotationY - rotationY;
      if (Math.abs(diff) < 0.2) {
        rotationY = targetRotationY;
        targetRotationY = null;
      } else {
        rotationY += diff * 0.14;
      }
    } else if (autoRotate && !isDragging) {
      rotationY = (rotationY + 0.45) % 360;
    } else if (!isDragging && Math.abs(dragVelocity) > 0.05) {
      rotationY = (rotationY + dragVelocity) % 360;
      dragVelocity *= 0.92; // Friction
    }

    render3DScene();
    requestAnimationFrame(tick);
  }

  // 3. Pointer & Drag Interaction (Apple WWDC Fluid Direct Manipulation)
  function initPointerDrag() {
    if (!viewport) return;

    viewport.addEventListener('pointerdown', (e) => {
      isDragging = true;
      targetRotationY = null;
      dragVelocity = 0;
      lastPointerX = e.clientX;
      lastPointerY = e.clientY;
      viewport.setPointerCapture(e.pointerId);
    });

    viewport.addEventListener('pointermove', (e) => {
      if (!isDragging) return;

      const deltaX = e.clientX - lastPointerX;
      const deltaY = e.clientY - lastPointerY;

      rotationY = (rotationY + deltaX * 0.65) % 360;
      rotationX = Math.max(-18, Math.min(22, rotationX + deltaY * -0.3));

      dragVelocity = deltaX * 0.45;
      lastPointerX = e.clientX;
      lastPointerY = e.clientY;

      // Unset active quick-angle pill during free drag
      document.querySelectorAll('.btn-angle').forEach(btn => btn.classList.remove('active'));
    });

    const endDrag = (e) => {
      if (!isDragging) return;
      isDragging = false;
      try {
        viewport.releasePointerCapture(e.pointerId);
      } catch (err) {}
    };

    viewport.addEventListener('pointerup', endDrag);
    viewport.addEventListener('pointercancel', endDrag);

    // Auto-rotate toggle
    const toggleBtn = document.getElementById('btnToggleAutoRotate');
    if (toggleBtn) {
      toggleBtn.addEventListener('click', () => {
        autoRotate = !autoRotate;
        toggleBtn.classList.toggle('active', autoRotate);
        if (typeof window.showToast === 'function') {
          window.showToast(autoRotate ? 'Авто-вращение включено' : 'Авто-вращение приостановлено', 'info');
        }
      });
    }

    // Quick angle buttons
    document.querySelectorAll('.btn-angle').forEach(btn => {
      btn.addEventListener('click', () => {
        const angle = parseFloat(btn.dataset.angle) || 0;
        document.querySelectorAll('.btn-angle').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');

        // Smooth shortest path to target angle
        const currentMod = ((rotationY % 360) + 360) % 360;
        let diff = angle - currentMod;
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;

        targetRotationY = rotationY + diff;
        rotationX = 4;
        dragVelocity = 0;
      });
    });
  }

  // 4. Model Type Switcher (Steve 4px vs Alex 3px)
  function initModelSwitcher() {
    const steveBtn = document.getElementById('modelSteveBtn');
    const alexBtn = document.getElementById('modelAlexBtn');

    const setModel = (type) => {
      modelType = type;
      if (steveBtn) {
        const isSteve = type === 'classic';
        steveBtn.classList.toggle('active', isSteve);
        steveBtn.setAttribute('aria-checked', isSteve ? 'true' : 'false');
      }
      if (alexBtn) {
        const isAlex = type === 'slim';
        alexBtn.classList.toggle('active', isAlex);
        alexBtn.setAttribute('aria-checked', isAlex ? 'true' : 'false');
      }

      if (typeof window.showToast === 'function') {
        const name = type === 'classic' ? 'Классика (Steve, 4px)' : 'Слим (Alex, 3px)';
        window.showToast(`Выбрана модель: ${name}`, 'success');
      }
    };

    if (steveBtn) steveBtn.addEventListener('click', () => setModel('classic'));
    if (alexBtn) alexBtn.addEventListener('click', () => setModel('slim'));
  }

  // 5. Cape Selector
  function initCapeSelector() {
    const capeCards = document.querySelectorAll('.cape-card');
    const hudCapeBadge = document.getElementById('hudCapeBadge');
    const capeStatusTag = document.getElementById('capeStatusTag');

    capeCards.forEach(card => {
      card.addEventListener('click', () => {
        const cape = card.dataset.cape || 'none';
        currentCape = cape;

        capeCards.forEach(c => {
          const isActive = c === card;
          c.classList.toggle('active', isActive);
          c.setAttribute('aria-checked', isActive ? 'true' : 'false');
        });

        const capeLabels = {
          founder: 'Плащ Aura Founder',
          classic: 'Стандартный плащ',
          none: 'Без плаща'
        };

        const tagLabels = {
          founder: 'Активен: Founder',
          classic: 'Активен: Стандартный',
          none: 'Плащ скрыт'
        };

        if (hudCapeBadge) hudCapeBadge.textContent = capeLabels[cape] || 'Плащ Aura';
        if (capeStatusTag) capeStatusTag.textContent = tagLabels[cape] || 'Выбран';

        if (typeof window.showToast === 'function') {
          window.showToast(`Применён: ${capeLabels[cape]}`, 'success');
        }
      });
    });
  }

  // 6. Skin Upload & Validation
  function initSkinUpload() {
    const fileInput = document.getElementById('skinFileInput');
    const dropzone = document.getElementById('skinUploadDropzone');
    const uploadBtn = document.getElementById('btnTriggerSkinUpload');
    const resetBtn = document.getElementById('btnResetDefaultSkin');
    const displayLabel = document.getElementById('skinFileNameDisplay');

    if (uploadBtn && fileInput) {
      uploadBtn.addEventListener('click', () => fileInput.click());
    }

    function handleFile(file) {
      if (!file) return;

      if (!file.name.toLowerCase().endsWith('.png') && file.type !== 'image/png') {
        if (typeof window.showToast === 'function') {
          window.showToast('Ошибка: поддерживаются только изображения формата PNG', 'error');
        }
        return;
      }

      const reader = new FileReader();
      reader.onload = function (e) {
        const img = new Image();
        img.onload = function () {
          // Strict Minecraft skin validation
          const is64x64 = img.width === 64 && img.height === 64;
          const is64x32 = img.width === 64 && img.height === 32;

          if (!is64x64 && !is64x32) {
            if (typeof window.showToast === 'function') {
              window.showToast(`Ошибка: размер ${img.width}×${img.height}px недопустим. Требуется 64×64 или 64×32.`, 'error');
            }
            return;
          }

          customSkinLoaded = true;
          customSkinImage = img;

          if (displayLabel) {
            displayLabel.textContent = `Скин: ${file.name} (${img.width}×${img.height})`;
          }

          if (typeof window.showToast === 'function') {
            window.showToast(`Скин «${file.name}» успешно загружен и применён`, 'success');
          }
        };

        img.onerror = function () {
          if (typeof window.showToast === 'function') {
            window.showToast('Не удалось прочитать файл скина', 'error');
          }
        };

        img.src = e.target.result;
      };

      reader.readAsDataURL(file);
    }

    if (fileInput) {
      fileInput.addEventListener('change', () => {
        if (fileInput.files && fileInput.files[0]) {
          handleFile(fileInput.files[0]);
        }
      });
    }

    // Drag and Drop
    if (dropzone) {
      dropzone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropzone.classList.add('is-dragover');
      });

      dropzone.addEventListener('dragleave', () => {
        dropzone.classList.remove('is-dragover');
      });

      dropzone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropzone.classList.remove('is-dragover');
        if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0]) {
          handleFile(e.dataTransfer.files[0]);
        }
      });
    }

    // Reset skin button
    if (resetBtn) {
      resetBtn.addEventListener('click', () => {
        customSkinLoaded = false;
        customSkinImage = null;
        if (fileInput) fileInput.value = '';
        if (displayLabel) {
          displayLabel.textContent = 'Скин: default_coock0ld.png (64×64)';
        }
        if (typeof window.showToast === 'function') {
          window.showToast('Скин сброшен на стандартный скин Coock0ld', 'info');
        }
      });
    }
  }

  // 7. Nickname & Profile Management
  function initProfileManagement() {
    const nickInput = document.getElementById('wardrobeNickInput');
    const saveNickBtn = document.getElementById('btnSaveNickname');
    const badgeNick = document.getElementById('wardrobeBadgeNick');
    const copyIdBtn = document.getElementById('btnCopyProfileId');

    if (saveNickBtn && nickInput) {
      saveNickBtn.addEventListener('click', () => {
        const val = nickInput.value.trim();
        const validNickRegex = /^[a-zA-Z0-9_]{3,16}$/;

        if (!validNickRegex.test(val)) {
          if (typeof window.showToast === 'function') {
            window.showToast('Никнейм должен содержать от 3 до 16 символов (только латиница, цифры и _)', 'error');
          }
          return;
        }

        if (badgeNick) badgeNick.textContent = val;

        // Also update Coock0ld mentions across play/lobby if accessible
        document.querySelectorAll('.profile-nickname').forEach(el => el.textContent = val);

        if (typeof window.showToast === 'function') {
          window.showToast(`Никнейм успешно изменён на «${val}»`, 'success');
        }
      });
    }

    if (copyIdBtn) {
      copyIdBtn.addEventListener('click', () => {
        const idVal = document.getElementById('wardrobeProfileIdVal')?.textContent || 'AURA-7821-X9M2';
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(idVal).catch(() => {});
        }
        copyIdBtn.classList.add('is-copied');
        if (typeof window.showToast === 'function') {
          window.showToast('ID профиля скопирован в буфер', 'success');
        }
        setTimeout(() => {
          copyIdBtn.classList.remove('is-copied');
        }, 1800);
      });
    }
  }

  // Lifecycle initialization
  function initWardrobeScreen() {
    initPointerDrag();
    initModelSwitcher();
    initCapeSelector();
    initSkinUpload();
    initProfileManagement();

    // Set initial model button state
    const steveBtn = document.getElementById('modelSteveBtn');
    if (steveBtn) steveBtn.classList.add('active');

    // Start 3D animation loop
    tick();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initWardrobeScreen);
  } else {
    initWardrobeScreen();
  }

})();