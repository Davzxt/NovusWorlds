const B = window.BABYLON;

export async function createR6(scene, avatarData = {}, options = {}) {
  const root = new B.TransformNode('r6Root', scene);
  root.parts = {};
  root.avatarData = avatarData || {};
  const model = await B.SceneLoader.ImportMeshAsync('', '/assets/r6/', 'r6.gltf', scene);
  const container = new B.TransformNode('r6Model', scene);
  for (const mesh of model.meshes) {
    if (mesh === scene.meshes[0] && !mesh.geometry) continue;
    mesh.parent = container;
    if (mesh.material) mesh.material = mesh.material.clone(mesh.name + 'Mat');
    mesh.receiveShadows = true;
  }
  container.parent = root;
  container.scaling.setAll(options.scale || 1.35);
  mapParts(root, container);
  applyColors(root, avatarData);
  addFace(root, scene, avatarData);
  await addHats(root, scene, avatarData);
  return root;
}

export function animateR6(root, state, seconds, presets = {}) {
  const cfg = presets[state] || {};
  const speed = cfg.speed || (state === 'walk' ? 8 : state === 'climb' ? 10 : 2);
  const arm = cfg.arm ?? (state === 'walk' ? .5 : .65);
  const leg = cfg.leg ?? (state === 'walk' ? .42 : .35);
  const torso = cfg.torso ?? .03;
  const swing = Math.sin(seconds * speed);
  const parts = root.parts || {};
  for (const node of Object.values(parts)) {
    node.rotation.x *= .65;
    node.rotation.z *= .65;
  }
  if (parts.torso) parts.torso.rotation.z = state === 'walk' ? swing * torso : 0;
  if (state === 'jump' || state === 'fall') {
    setRot(parts.leftArm, arm || .55);
    setRot(parts.rightArm, arm || .55);
    setRot(parts.leftLeg, leg || -.25);
    setRot(parts.rightLeg, leg || -.25);
    return;
  }
  if (state === 'climb') {
    setRot(parts.leftArm, -swing * arm);
    setRot(parts.rightArm, swing * arm);
    setRot(parts.leftLeg, swing * leg);
    setRot(parts.rightLeg, -swing * leg);
    return;
  }
  if (state === 'walk') {
    setRot(parts.leftArm, -swing * arm);
    setRot(parts.rightArm, swing * arm);
    setRot(parts.leftLeg, swing * leg);
    setRot(parts.rightLeg, -swing * leg);
  }
}

export function applyHatTransform(node, t = {}) {
  const p = t.position || { x: 0, y: 3.38, z: 0 };
  const r = t.rotation || { x: 0, y: 0, z: 0 };
  const s = t.scale || { x: 1, y: 1, z: 1 };
  node.position.set(p.x || 0, p.y || 3.38, p.z || 0);
  node.rotation.set(B.Tools.ToRadians(r.x || 0), B.Tools.ToRadians(r.y || 0), B.Tools.ToRadians(r.z || 0));
  node.scaling.set(s.x || 1, s.y || 1, s.z || 1);
}

export async function addPreviewHat(root, scene, modelUrl, transform) {
  const old = root.getChildren().find(c => c.name === 'previewHat');
  if (old) old.dispose(false, true);
  const holder = new B.TransformNode('previewHat', scene);
  holder.parent = root;
  if (modelUrl) {
    try {
      const url = new URL(modelUrl, location.href);
      const isBlob = url.protocol === 'blob:' || url.protocol === 'data:';
      const path = isBlob ? '' : url.pathname.slice(0, url.pathname.lastIndexOf('/') + 1);
      const file = isBlob ? modelUrl : url.pathname.slice(url.pathname.lastIndexOf('/') + 1);
      const loaded = await B.SceneLoader.ImportMeshAsync('', path, file, scene);
      loaded.meshes.forEach(m => { if (m.geometry) m.parent = holder; });
    } catch {
      makeDefaultHat(scene, holder);
    }
  } else {
    makeDefaultHat(scene, holder);
  }
  applyHatTransform(holder, transform);
  return holder;
}

function mapParts(root, model) {
  const names = [
    ['head', /head/i],
    ['torso', /torso|upper|body/i],
    ['leftArm', /left.*arm|arm.*left/i],
    ['rightArm', /right.*arm|arm.*right/i],
    ['leftLeg', /left.*leg|leg.*left/i],
    ['rightLeg', /right.*leg|leg.*right/i]
  ];
  const meshes = model.getChildMeshes().filter(m => m.geometry);
  for (const [key, rx] of names) {
    const mesh = meshes.find(m => rx.test(m.name));
    if (!mesh) continue;
    const pivot = new B.TransformNode(key + 'Pivot', root.getScene());
    pivot.parent = model;
    pivot.position.copyFrom(mesh.position);
    mesh.parent = pivot;
    mesh.position.subtractInPlace(pivot.position);
    root.parts[key] = pivot;
  }
  if (Object.keys(root.parts).length < 6 && meshes.length >= 6) {
    const byY = [...meshes].sort((a, b) => a.position.y - b.position.y);
    const legs = byY.slice(0, 2).sort((a, b) => a.position.x - b.position.x);
    const mid = byY.slice(2, 5);
    const arms = mid.filter(m => Math.abs(m.position.x) > .45).sort((a, b) => a.position.x - b.position.x);
    const torso = mid.find(m => Math.abs(m.position.x) <= .45);
    const head = byY[5];
    assign('leftLeg', legs[0]);
    assign('rightLeg', legs[1]);
    assign('torso', torso);
    assign('leftArm', arms[0]);
    assign('rightArm', arms[1]);
    assign('head', head);
  }
  function assign(key, mesh) {
    if (!mesh || root.parts[key]) return;
    const pivot = new B.TransformNode(key + 'Pivot', root.getScene());
    pivot.parent = model;
    pivot.position.copyFrom(mesh.position);
    mesh.parent = pivot;
    mesh.position.subtractInPlace(pivot.position);
    root.parts[key] = pivot;
  }
}

function applyColors(root, avatarData = {}) {
  const colors = avatarData.colors || {};
  const pick = name => {
    name = name.toLowerCase();
    if (name.includes('head')) return colors.head || '#f5cd30';
    if (name.includes('arm')) return colors.arms || '#f5cd30';
    if (name.includes('leg')) return colors.legs || '#1b2a35';
    return colors.torso || '#0d69ac';
  };
  root.getChildMeshes().forEach(mesh => {
    const mat = new B.StandardMaterial(mesh.name + 'Color', root.getScene());
    mat.diffuseColor = B.Color3.FromHexString(pick(mesh.name));
    mat.specularColor = new B.Color3(.08, .08, .08);
    mesh.material = mat;
  });
}

function addFace(root, scene, avatarData = {}) {
  const item = (avatarData.equippedItems || []).find(i => i.type === 'face' && i.asset_url);
  if (item?.asset_url?.startsWith('data:image')) {
    const plane = B.MeshBuilder.CreatePlane('facePlane', { width: .85, height: .85 }, scene);
    const mat = new B.StandardMaterial('facePixelMat', scene);
    mat.diffuseTexture = new B.Texture(item.asset_url, scene, false, false, B.Texture.NEAREST_SAMPLINGMODE);
    mat.diffuseTexture.hasAlpha = true;
    mat.useAlphaFromDiffuseTexture = true;
    plane.material = mat;
    plane.parent = root;
    plane.position.set(0, 3.02, -.69);
    return;
  }
  const mat = new B.StandardMaterial('faceMat', scene);
  mat.diffuseColor = B.Color3.Black();
  for (const [name, x, y, w, h] of [['eyeL', -.18, 3.06, .08, .08], ['eyeR', .18, 3.06, .08, .08], ['mouth', 0, 2.84, .3, .045]]) {
    const m = B.MeshBuilder.CreateBox(name, { width: w, height: h, depth: .02 }, scene);
    m.parent = root;
    m.position.set(x, y, -.7);
    m.material = mat;
  }
}

async function addHats(root, scene, avatarData = {}) {
  for (const item of (avatarData.equippedItems || []).filter(i => i.type === 'hat').slice(0, 3)) {
    await addPreviewHat(root, scene, item.model_url, item.hat_transform || {});
  }
}

function makeDefaultHat(scene, parent) {
  const mat = new B.StandardMaterial('defaultHatMat', scene);
  mat.diffuseColor = B.Color3.Black();
  const brim = B.MeshBuilder.CreateBox('hatBrim', { width: 1.25, height: .18, depth: 1.25 }, scene);
  const top = B.MeshBuilder.CreateBox('hatTop', { width: .85, height: .35, depth: .85 }, scene);
  brim.parent = parent; top.parent = parent; top.position.y = .25;
  brim.material = mat; top.material = mat;
}

function setRot(node, x) {
  if (node) node.rotation.x = x;
}
