import * as THREE from '/vendor/three/build/three.module.js';
import { GLTFLoader } from '/vendor/three/examples/jsm/loaders/GLTFLoader.js';
import { OrbitControls } from '/vendor/three/examples/jsm/controls/OrbitControls.js';

export const loader = new GLTFLoader();

export function createR6Viewer(container, options = {}) {
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(options.background || '#777777');
  const camera = new THREE.PerspectiveCamera(45, container.clientWidth / Math.max(1, container.clientHeight), .1, 200);
  camera.position.set(4, 3.2, 6);
  const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
  renderer.setSize(container.clientWidth, container.clientHeight);
  container.innerHTML = '';
  container.appendChild(renderer.domElement);
  const controls = new OrbitControls(camera, renderer.domElement);
  controls.target.set(0, 1.4, 0);
  controls.update();
  scene.add(new THREE.HemisphereLight(0xffffff, 0x333333, 2));
  const dir = new THREE.DirectionalLight(0xffffff, 1.4);
  dir.position.set(5, 8, 4);
  scene.add(dir);
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(18, 18), new THREE.MeshStandardMaterial({ color: '#999999' }));
  floor.rotation.x = -Math.PI / 2;
  scene.add(floor);
  const avatar = new THREE.Group();
  scene.add(avatar);
  loader.load('/assets/r6/r6.gltf', (gltf) => {
    avatar.add(gltf.scene);
    applyAvatarColors(avatar, options.avatar);
    avatar.add(createFaceOverlay(options.avatar, true));
    applyHats(avatar, options.avatar);
  }, undefined, () => avatar.add(blockR6(options.avatar)));
  let hat = null;
  function setHatTransform(t = {}) {
    if (!hat) {
      hat = new THREE.Mesh(new THREE.BoxGeometry(1.2, .28, 1.2), new THREE.MeshStandardMaterial({ color: '#111111' }));
      avatar.add(hat);
    }
    const p = t.position || { x: 0, y: 2.85, z: 0 }, r = t.rotation || { x: 0, y: 0, z: 0 }, s = t.scale || { x: 1, y: 1, z: 1 };
    hat.position.set(p.x, p.y, p.z);
    hat.rotation.set(THREE.MathUtils.degToRad(r.x || 0), THREE.MathUtils.degToRad(r.y || 0), THREE.MathUtils.degToRad(r.z || 0));
    hat.scale.set(s.x || 1, s.y || 1, s.z || 1);
  }
  if (options.item?.type === 'hat') setHatTransform(options.item.hat_transform || JSON.parse(options.item.hat_transform || '{}'));
  function animate() {
    requestAnimationFrame(animate);
    if (options.spin !== false) avatar.rotation.y += .006;
    renderer.render(scene, camera);
  }
  animate();
  addEventListener('resize', () => {
    if (!container.isConnected) return;
    camera.aspect = container.clientWidth / Math.max(1, container.clientHeight);
    camera.updateProjectionMatrix();
    renderer.setSize(container.clientWidth, container.clientHeight);
  });
  return { scene, camera, renderer, avatar, setHatTransform };
}

export function blockR6(avatarData = {}) {
  const colors = avatarData?.colors || {};
  const group = new THREE.Group();
  const part = (name, size, pos, color) => {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(size[0], size[1], size[2]), new THREE.MeshStandardMaterial({ color }));
    mesh.name = name; mesh.position.set(pos[0], pos[1], pos[2]); group.add(mesh); return mesh;
  };
  part('left_leg', [.5, 1, .5], [-.25, .5, 0], colors.legs || '#1b2a35');
  part('right_leg', [.5, 1, .5], [.25, .5, 0], colors.legs || '#1b2a35');
  part('torso', [1, 1.1, .5], [0, 1.55, 0], colors.torso || '#0d69ac');
  part('left_arm', [.45, 1.1, .5], [-.75, 1.55, 0], colors.arms || '#f5cd30');
  part('right_arm', [.45, 1.1, .5], [.75, 1.55, 0], colors.arms || '#f5cd30');
  part('head', [.9, .9, .9], [0, 2.55, 0], colors.head || '#f5cd30');
  group.add(createFaceOverlay(avatarData, false));
  applyHats(group, avatarData);
  return group;
}

export async function createR6Avatar(avatarData = {}) {
  const group = new THREE.Group();
  const gltf = await loader.loadAsync('/assets/r6/r6.gltf');
  const model = gltf.scene;
  model.scale.setScalar(1.35);
  group.add(model);
  applyAvatarColors(group, avatarData);
  group.add(createFaceOverlay(avatarData, true));
  applyHats(group, avatarData);
  return group;
}

export function applyHats(root, avatarData = {}) {
  for (const old of [...root.children].filter(c => c.name === 'novus_hat')) old.removeFromParent();
  for (const item of (avatarData.equippedItems || []).filter(i => i.type === 'hat').slice(0, 3)) {
    const t = item.hat_transform || {};
    const p = t.position || { x: 0, y: 2.85, z: 0 };
    const r = t.rotation || { x: 0, y: 0, z: 0 };
    const s = t.scale || { x: 1, y: 1, z: 1 };
    const brim = new THREE.Mesh(new THREE.BoxGeometry(1.25, .18, 1.25), new THREE.MeshStandardMaterial({ color: '#111111' }));
    const top = new THREE.Mesh(new THREE.BoxGeometry(.85, .35, .85), new THREE.MeshStandardMaterial({ color: '#222222' }));
    const hat = new THREE.Group();
    hat.name = 'novus_hat';
    top.position.y = .25;
    hat.add(brim, top);
    hat.position.set(p.x, p.y, p.z);
    hat.rotation.set(THREE.MathUtils.degToRad(r.x || 0), THREE.MathUtils.degToRad(r.y || 0), THREE.MathUtils.degToRad(r.z || 0));
    hat.scale.set(s.x || 1, s.y || 1, s.z || 1);
    root.add(hat);
  }
}

export function createFaceOverlay(avatarData = {}, gltfRig = false) {
  const face = new THREE.Group();
  const eyeMat = new THREE.MeshBasicMaterial({ color: '#111111' });
  const mouthMat = new THREE.MeshBasicMaterial({ color: avatarData.face === 'Serious Face' ? '#333333' : '#8b1a1a' });
  const eye1 = new THREE.Mesh(new THREE.BoxGeometry(.08, .08, .02), eyeMat);
  const eye2 = eye1.clone();
  const mouth = new THREE.Mesh(new THREE.BoxGeometry(avatarData.face === 'Chill Face' ? .34 : .28, .05, .02), mouthMat);
  const y = gltfRig ? 3.03 : 2.62;
  const mouthY = gltfRig ? 2.82 : (avatarData.face === 'Serious Face' ? 2.42 : 2.38);
  const z = gltfRig ? -0.68 : -0.61;
  eye1.position.set(-.18, y, z); eye2.position.set(.18, y, z); mouth.position.set(0, mouthY, z);
  face.add(eye1, eye2, mouth);
  face.name = 'face_overlay';
  return face;
}

function applyAvatarColors(root, avatarData = {}) {
  const colors = avatarData?.colors || {};
  root.traverse((obj) => {
    if (!obj.isMesh) return;
    obj.material = obj.material.clone();
    const n = obj.name.toLowerCase();
    if (n.includes('head') || n.includes('pyramid')) obj.material.color.set(colors.head || '#f5cd30');
    else if (n.includes('arm')) obj.material.color.set(colors.arms || '#f5cd30');
    else if (n.includes('leg') || n === 'mesh') obj.material.color.set(colors.legs || '#1b2a35');
    else obj.material.color.set(colors.torso || '#0d69ac');
  });
}
