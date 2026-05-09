import { addPreviewHat, applyHatTransform, createR6 } from './babylon-r6.js';

const B = window.BABYLON;

export async function createBabylonR6Viewer(container, options = {}) {
  if (!B) throw new Error('Babylon.js nao carregou.');
  const canvas = document.createElement('canvas');
  canvas.style.width = '100%';
  canvas.style.height = '100%';
  container.innerHTML = '';
  container.appendChild(canvas);

  const engine = new B.Engine(canvas, true, { preserveDrawingBuffer: true });
  const scene = new B.Scene(engine);
  scene.clearColor = B.Color4.FromHexString((options.background || '#777777') + 'ff');
  const camera = new B.ArcRotateCamera('previewCamera', Math.PI / 4, 1.15, 6.8, new B.Vector3(0, 1.8, 0), scene);
  camera.attachControl(canvas, true);
  camera.lowerRadiusLimit = 3.8;
  camera.upperRadiusLimit = 10;
  const hemi = new B.HemisphericLight('previewAmbient', new B.Vector3(0, 1, 0), scene);
  hemi.intensity = .8;
  const sun = new B.DirectionalLight('previewSun', new B.Vector3(-.4, -1, -.3), scene);
  sun.position.set(5, 8, 4);
  sun.intensity = 1.3;
  const shadows = new B.ShadowGenerator(1024, sun);
  shadows.usePercentageCloserFiltering = true;
  const floor = B.MeshBuilder.CreateGround('previewFloor', { width: 14, height: 14 }, scene);
  const floorMat = new B.StandardMaterial('previewFloorMat', scene);
  floorMat.diffuseColor = B.Color3.FromHexString('#999999');
  floor.material = floorMat;
  floor.receiveShadows = true;

  const avatar = await createR6(scene, options.avatar || {}, { scale: 1.35 });
  avatar.getChildMeshes().forEach(m => shadows.addShadowCaster(m));
  let previewHat = null;

  async function setHatModelUrl(url) {
    previewHat = await addPreviewHat(avatar, scene, url, {});
    previewHat.getChildMeshes?.().forEach(m => shadows.addShadowCaster(m));
  }

  function setHatTransform(transform) {
    if (!previewHat) setHatModelUrl(null).then(() => applyHatTransform(previewHat, transform));
    else applyHatTransform(previewHat, transform);
  }

  function setFaceTexture(dataUrl) {
    const old = avatar.getChildren().find(c => c.name === 'facePlane');
    if (old) old.dispose();
    const plane = B.MeshBuilder.CreatePlane('facePlane', { width: .85, height: .85 }, scene);
    const mat = new B.StandardMaterial('facePixelMat', scene);
    mat.diffuseTexture = new B.Texture(dataUrl, scene, false, false, B.Texture.NEAREST_SAMPLINGMODE);
    mat.diffuseTexture.hasAlpha = true;
    mat.useAlphaFromDiffuseTexture = true;
    plane.material = mat;
    plane.parent = avatar;
    plane.position.set(0, 3.02, -.69);
  }

  engine.runRenderLoop(() => {
    if (options.spin !== false) avatar.rotation.y += .006;
    scene.render();
  });
  addEventListener('resize', () => {
    if (!container.isConnected) return;
    engine.resize();
  });

  await setHatModelUrl(null);
  return { engine, scene, camera, avatar, setHatModelUrl, setHatTransform, setFaceTexture };
}
