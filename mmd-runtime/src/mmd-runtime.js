import { Engine } from "@babylonjs/core/Engines/engine";
import { DirectionalLight } from "@babylonjs/core/Lights/directionalLight";
import { HemisphericLight } from "@babylonjs/core/Lights/hemisphericLight";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Vector3 } from "@babylonjs/core/Maths/math.vector";
import { LoadAssetContainerAsync } from "@babylonjs/core/Loading/sceneLoader";
import { Scene } from "@babylonjs/core/scene";
import {
  MmdCamera,
  MmdMesh,
  MmdStandardMaterialProxy,
} from "babylon-mmd/esm/Runtime";
import { StreamAudioPlayer } from "babylon-mmd/esm/Runtime/Audio/streamAudioPlayer";
import { GetMmdWasmInstance } from "babylon-mmd/esm/Runtime/Optimized/mmdWasmInstance";
import { MmdWasmInstanceTypeSPR } from "babylon-mmd/esm/Runtime/Optimized/InstanceType/singlePhysicsRelease";
import { MmdWasmPhysics } from "babylon-mmd/esm/Runtime/Optimized/Physics/mmdWasmPhysics";
import { MmdWasmRuntime } from "babylon-mmd/esm/Runtime/Optimized/mmdWasmRuntime";
import { SdefInjector, VmdLoader } from "babylon-mmd/esm/Loader";
import "babylon-mmd/esm/Loader/pmxLoader";
import "babylon-mmd/esm/Runtime/Animation/mmdRuntimeModelAnimation";

const canvas = document.getElementById("renderCanvas");

if (!(canvas instanceof HTMLCanvasElement)) {
  throw new Error("MMD canvas was not found.");
}

const engine = new Engine(canvas, true, {
  alpha: true,
  antialias: true,
  preserveDrawingBuffer: true,
  premultipliedAlpha: false,
  stencil: true,
});

SdefInjector.OverrideEngineCreateEffect(engine);

const scene = new Scene(engine);
scene.clearColor = new Color4(0, 0, 0, 0);
scene.ambientColor = new Color3(0.2, 0.2, 0.2);

const fillLight = new HemisphericLight("mmdFillLight", new Vector3(0, 1, 0), scene);
fillLight.intensity = 0.6;
fillLight.groundColor = new Color3(0.35, 0.35, 0.35);

const keyLight = new DirectionalLight("mmdKeyLight", new Vector3(0.25, -0.85, -0.65), scene);
keyLight.intensity = 0.8;

let runtime = null;
let vmdLoader = null;
let audioPlayer = null;

const state = {
  camera: null,
  container: null,
  model: null,
  secondaryContainer: null,
  secondaryModel: null,
  preloadedSecondary: null,
  secondaryPreloadPromise: null,
  secondaryPreloadIndex: -1,
  secondaryPreloadUrl: "",
  modelIndex: -1,
  secondaryModelIndex: -1,
  modelUrl: "",
  secondaryModelUrl: "",
  motionHandle: null,
  secondaryMotionHandle: null,
  motionCache: new Map(),
  motionLoadPromises: new Map(),
  cameraFitScale: 1,
  loopCurrentMotion: false,
  loopEndFrame: 0,
  userPauseRequested: false,
  loopRestarting: false,
  blinkIndices: [],
  blinkTimer: null,
  musicUrl: "",
};

const desktopBodyTonePresets = {
  brighter: new Color3(0.84, 0.74, 0.72),
  lighter: new Color3(0.78, 0.66, 0.64),
  deep: new Color3(0.62, 0.48, 0.46),
};
const desktopBodyDiffuse = desktopBodyTonePresets.brighter.clone();
const desktopFaceDiffuse = new Color3(0.9744, 0.8288, 0.8064);
const desktopBodyAmbient = new Color3(0.02, 0.015, 0.015);
let desktopBodyTone = "brighter";

function updateDesktopToneColors(preset) {
  desktopBodyDiffuse.copyFrom(preset);
  // [FACE]Base is authored darker than the [BODY] atlas. Compensate its
  // diffuse multiplier so the rendered face stays in the same tone band as
  // the arms and torso instead of looking one preset darker.
  desktopFaceDiffuse.copyFromFloats(
    Math.min(1, preset.r * 1.16),
    Math.min(1, preset.g * 1.12),
    Math.min(1, preset.b * 1.12));
}

function setDesktopBodyTone(value, requestId) {
  const requestedTone = String(value ?? "brighter").toLowerCase();
  const tone = Object.hasOwn(desktopBodyTonePresets, requestedTone)
    ? requestedTone
    : "brighter";
  const preset = desktopBodyTonePresets[tone];
  updateDesktopToneColors(preset);
  desktopBodyTone = tone;
  applyDesktopPetMaterialPresentation();
  post("skin-tone-updated", { tone: desktopBodyTone, requestId });
}

function isDesktopSkinMaterial(material) {
  const name = String(material?.name ?? "").toLowerCase();
  return (name.startsWith("[body]") && !name.includes("pubic"))
    || isDesktopFaceBaseMaterial(material);
}

function isDesktopFaceBaseMaterial(material) {
  return String(material?.name ?? "").toLowerCase() === "[face]base";
}

function applyDesktopPetMaterialPresentation() {
  for (const container of activeContainers()) {
    for (const material of container.materials) {
      if (!isDesktopSkinMaterial(material)) {
        continue;
      }

      const diffuse = isDesktopFaceBaseMaterial(material)
        ? desktopFaceDiffuse
        : desktopBodyDiffuse;
      if (!material.diffuseColor.equals(diffuse)) {
        material.diffuseColor.copyFrom(diffuse);
      }
      if (!material.ambientColor.equals(desktopBodyAmbient)) {
        material.ambientColor.copyFrom(desktopBodyAmbient);
      }
    }
  }
}

function activeContainers() {
  return [state.container, state.secondaryContainer].filter((container) => container !== null);
}

function activeModels() {
  return [state.model, state.secondaryModel].filter((model) => model !== null);
}

function restartLoopIfNeeded() {
    if (!state.loopCurrentMotion
      || state.userPauseRequested
      || state.loopRestarting
      || !runtime
      || runtime.isAnimationPlaying) {
      return;
    }

    const duration = state.loopEndFrame > 0
      ? state.loopEndFrame
      : runtime.animationFrameTimeDuration;
    if (!Number.isFinite(duration)
      || duration <= 0
      || runtime.currentFrameTime < duration - 0.5) {
      return;
    }

    state.loopRestarting = true;
    runtime.seekAnimation(0, true)
      .then(() => runtime.playAnimation())
      .then(() => {
        post("motion-looped", {
          frameTime: runtime.currentFrameTime,
          duration,
        });
      })
      .catch((error) => {
        const exception = error instanceof Error ? error : new Error(String(error));
        post("runtime-error", {
          operation: "loop-motion",
          message: exception.message,
          stack: exception.stack ?? "",
        });
      })
      .finally(() => {
        state.loopRestarting = false;
      });
}

function installAnimationLoopObserver() {
  runtime.onPauseAnimationObservable.add(restartLoopIfNeeded);
  // The pause observable is the normal path. This render-side check is a
  // fallback for runtimes that finish a short VMD between observable ticks.
  scene.onBeforeRenderObservable.add(restartLoopIfNeeded);
}

function waitForAudioMetadata(player) {
  if (player.metadataLoaded && Number.isFinite(player.duration) && player.duration > 0) {
    return Promise.resolve(player.duration);
  }

  return new Promise((resolve, reject) => {
    let settled = false;
    let timeoutId = null;
    let durationObserver = null;
    let errorObserver = null;

    const finish = (callback, value) => {
      if (settled) {
        return;
      }

      settled = true;
      if (timeoutId !== null) {
        window.clearTimeout(timeoutId);
      }
      if (durationObserver !== null) {
        player.onDurationChangedObservable.remove(durationObserver);
      }
      if (errorObserver !== null) {
        player.onLoadErrorObservable.remove(errorObserver);
      }
      callback(value);
    };

    durationObserver = player.onDurationChangedObservable.add(() => {
      if (Number.isFinite(player.duration) && player.duration > 0) {
        finish(resolve, player.duration);
      }
    });
    errorObserver = player.onLoadErrorObservable.add(() => {
      finish(reject, new Error("The selected music file could not be decoded by WebView2."));
    });
    timeoutId = window.setTimeout(() => {
      finish(reject, new Error("Timed out while loading the selected music file."));
    }, 15000);

    if (player.metadataLoaded && Number.isFinite(player.duration) && player.duration > 0) {
      finish(resolve, player.duration);
    }
  });
}

async function loadMusic(url, requestId) {
  if (!runtime || !audioPlayer) {
    throw new Error("The MMD audio player is still initializing.");
  }

  const normalizedUrl = url ? normalizeModelUrl(url) : "";
  state.musicUrl = normalizedUrl;
  await runtime.setAudioPlayer(null);
  audioPlayer.pause();

  if (!normalizedUrl) {
    post("music-cleared", {
      enabled: false,
      requestId,
    });
    return;
  }

  audioPlayer.source = normalizedUrl;
  const duration = await waitForAudioMetadata(audioPlayer);
  await runtime.setAudioPlayer(audioPlayer);
  post("music-loaded", {
    enabled: true,
    url: normalizedUrl,
    duration,
    requestId,
  });
}

async function initializeMmdRuntime() {
  const wasmInstanceType = new MmdWasmInstanceTypeSPR();
  const wasmBindgen = wasmInstanceType.getWasmInstanceInner();
  const wasmInstance = await GetMmdWasmInstance({
    getWasmInstanceInner: () => ({
      ...wasmBindgen,
      default: () => wasmBindgen.default(new URL("mmd-spr.wasm", document.baseURI)),
    }),
  }, 1);
  const physics = new MmdWasmPhysics(scene);
  runtime = new MmdWasmRuntime(wasmInstance, scene, physics);
  runtime.loggingEnabled = true;
  runtime.physics?.setGravity(new Vector3(0, -98, 0));
  if (runtime.physics) {
    runtime.physics.maxSubSteps = 4;
    runtime.physics.fixedTimeStep = 1 / 60;
  }
  runtime.register(scene);
  audioPlayer = new StreamAudioPlayer(scene, { pool: true });
  audioPlayer.volume = 0.7;
  audioPlayer.onLoadErrorObservable.add(() => {
    post("music-error", {
      url: state.musicUrl,
      message: "The selected music file could not be decoded by WebView2.",
    });
  });
  installAnimationLoopObserver();
  scene.onBeforeRenderObservable.add(applyDesktopPetMaterialPresentation);

  vmdLoader = new VmdLoader(scene);
  vmdLoader.loggingEnabled = true;
  window.HimeMmdRuntime.runtime = runtime;
  window.HimeMmdRuntime.vmdLoader = vmdLoader;
  window.HimeMmdRuntime.audioPlayer = audioPlayer;

  post("runtime-ready", {
    runtime: "babylon-mmd-wasm",
    runtimeVersion: "1.3.0",
    physics: "babylon-mmd-wasm-integrated",
  });
}

function post(type, payload = {}) {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({ type, ...payload });
  }
}

function normalizeModelUrl(url) {
  return String(url).replaceAll("\\", "/");
}

function finiteBounds(meshes) {
  const min = new Vector3(Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY);
  const max = new Vector3(Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY);
  let found = false;

  for (const mesh of meshes) {
    if (!mesh || typeof mesh.getTotalVertices !== "function" || mesh.getTotalVertices() <= 0) {
      continue;
    }

    const bounds = mesh.getHierarchyBoundingVectors(true);
    if (!Number.isFinite(bounds.min.x) || !Number.isFinite(bounds.max.x)) {
      continue;
    }

    min.x = Math.min(min.x, bounds.min.x);
    min.y = Math.min(min.y, bounds.min.y);
    min.z = Math.min(min.z, bounds.min.z);
    max.x = Math.max(max.x, bounds.max.x);
    max.y = Math.max(max.y, bounds.max.y);
    max.z = Math.max(max.z, bounds.max.z);
    found = true;
  }

  if (!found) {
    throw new Error("MMD model has no renderable geometry.");
  }

  return { min, max };
}

function setCameraForModel(meshes, fitScale = 1) {
  const bounds = finiteBounds(meshes);
  const width = Math.max(0.1, bounds.max.x - bounds.min.x);
  const height = Math.max(0.1, bounds.max.y - bounds.min.y);
  const center = new Vector3(
    (bounds.min.x + bounds.max.x) * 0.5,
    bounds.min.y + height * 0.54,
    (bounds.min.z + bounds.max.z) * 0.5,
  );

  if (state.camera) {
    state.camera.dispose();
  }

  const camera = new MmdCamera("mmdCamera", center, scene);
  camera.minZ = 0.01;
  camera.maxZ = 10000;
  camera.fov = 30 * Math.PI / 180;
  camera.target.copyFrom(center);
  camera.rotation.set(0, 0, 0);

  const aspect = Math.max(0.1, engine.getRenderWidth() / Math.max(1, engine.getRenderHeight()));
  const halfFov = camera.fov * 0.5;
  const verticalDistance = height * 0.5 / Math.tan(halfFov);
  const horizontalDistance = width * 0.5 / Math.tan(halfFov) / aspect;
  camera.distance = -Math.max(28, verticalDistance, horizontalDistance) * 0.52 * fitScale;
  camera.updatePosition();
  scene.activeCamera = camera;
  state.camera = camera;
  state.cameraFitScale = fitScale;
}

function findBlinkIndices(morphs) {
  const keywords = ["まばたき", "瞬き", "blink", "wink", "闭眼", "眨眼"];
  return morphs
    .filter((morph) => keywords.some((keyword) => morph.name.toLowerCase().includes(keyword.toLowerCase())))
    .map((morph) => morph.index);
}

function normalizeMorphName(value) {
  return String(value ?? "")
    .normalize("NFKC")
    .trim()
    .toLowerCase()
    .replace(/\s+/g, "");
}

function isFullNudeMorphName(value) {
  const normalized = normalizeMorphName(value);
  return normalized.includes("全裸")
    || normalized.includes("naked")
    || normalized.includes("nude");
}

function findMatchingMorphIndex(morphs, sourceMorph) {
  const sourceNames = [sourceMorph?.name, sourceMorph?.englishName]
    .map(normalizeMorphName)
    .filter((name) => name.length > 0);
  if (sourceNames.length === 0) {
    return -1;
  }

  const exactIndex = morphs.findIndex((morph) => {
    const targetNames = [morph?.name, morph?.englishName].map(normalizeMorphName);
    return targetNames.some((name) => name.length > 0 && sourceNames.includes(name));
  });
  if (exactIndex >= 0) {
    return exactIndex;
  }

  // Some PMX files localize the same full-nude morph differently. Keep this
  // fallback narrow so unrelated morphs are never changed accidentally.
  if (sourceNames.some(isFullNudeMorphName)) {
    return morphs.findIndex((morph) =>
      isFullNudeMorphName(morph?.name) || isFullNudeMorphName(morph?.englishName));
  }

  return -1;
}

function applyMorph(index, weight) {
  if (!state.model || !Number.isInteger(index)) {
    return { appliedModelCount: 0, targetMorphIndices: [] };
  }

  const sourceMorphs = state.model.morph.morphs;
  if (index < 0 || index >= sourceMorphs.length) {
    return { appliedModelCount: 0, targetMorphIndices: [] };
  }

  const clampedWeight = Math.max(0, Math.min(1, Number(weight) || 0));
  const sourceMorph = sourceMorphs[index];
  const targetMorphIndices = [];
  let appliedModelCount = 0;

  for (const activeModel of activeModels()) {
    const targetIndex = activeModel === state.model
      ? index
      : findMatchingMorphIndex(activeModel.morph.morphs, sourceMorph);
    if (targetIndex < 0) {
      targetMorphIndices.push(-1);
      continue;
    }

    activeModel.morph.setMorphWeightFromIndex(targetIndex, clampedWeight);
    activeModel.morph.update();
    targetMorphIndices.push(targetIndex);
    appliedModelCount += 1;
  }

  applyDesktopPetMaterialPresentation();
  return {
    appliedModelCount,
    sourceMorphName: sourceMorph?.name ?? "",
    sourceMorphEnglishName: sourceMorph?.englishName ?? "",
    targetMorphIndices,
  };
}

function stopBlink() {
  if (state.blinkTimer !== null) {
    window.clearInterval(state.blinkTimer);
    state.blinkTimer = null;
  }

  for (const index of state.blinkIndices) {
    applyMorph(index, 0);
  }
}

function startBlink(morphs) {
  stopBlink();
  state.blinkIndices = findBlinkIndices(morphs);
  if (state.blinkIndices.length === 0) {
    return;
  }

  state.blinkTimer = window.setInterval(() => {
    for (const index of state.blinkIndices) {
      applyMorph(index, 1);
    }

    window.setTimeout(() => {
      for (const index of state.blinkIndices) {
        applyMorph(index, 0);
      }
    }, 105);
  }, 5200);
}

function disposeCurrentModel() {
  stopBlink();
  state.loopCurrentMotion = false;
  state.loopEndFrame = 0;
  state.userPauseRequested = false;
  state.loopRestarting = false;

  runtime?.pauseAnimation();

  disposePreloadedSecondary();

  if (runtime) {
    if (state.model) {
      runtime.destroyMmdModel(state.model);
    }
    if (state.secondaryModel) {
      runtime.destroyMmdModel(state.secondaryModel);
    }
    runtime.setManualAnimationDuration(null);
  }

  for (const container of activeContainers()) {
    container.removeAllFromScene();
    container.dispose();
  }

  state.model = null;
  state.secondaryModel = null;
  state.container = null;
  state.secondaryContainer = null;
  state.motionHandle = null;
  state.secondaryMotionHandle = null;
  state.modelIndex = -1;
  state.secondaryModelIndex = -1;
  state.modelUrl = "";
  state.secondaryModelUrl = "";
  state.secondaryPreloadPromise = null;
  state.secondaryPreloadIndex = -1;
  state.secondaryPreloadUrl = "";
}

function describeMorphs(metadata) {
  return metadata.morphs.map((morph, index) => ({
    index,
    name: morph.name,
    englishName: morph.englishName,
    category: morph.category,
    type: morph.type,
    effectMagnitude: morphEffectMagnitude(morph),
  }));
}

function morphEffectMagnitude(morph) {
  let magnitude = 0;
  const measureArray = (values) => {
    if (!values) {
      return;
    }
    for (const value of values) {
      magnitude = Math.max(magnitude, Math.abs(Number(value) || 0));
    }
  };

  measureArray(morph.ratios);
  measureArray(morph.positions);
  measureArray(morph.rotations);
  measureArray(morph.offsets);
  if (morph.elements) {
    for (const element of morph.elements) {
      measureArray(element.diffuse);
      measureArray(element.specular);
      measureArray(element.ambient);
      measureArray(element.edgeColor);
      measureArray(element.textureColor);
      measureArray(element.sphereTextureColor);
      measureArray(element.toonTextureColor);
      magnitude = Math.max(magnitude, Math.abs(Number(element.shininess) || 0));
      magnitude = Math.max(magnitude, Math.abs(Number(element.edgeSize) || 0));
    }
  }

  return Number(magnitude.toFixed(6));
}

function configureDesktopPetMaterials(materials) {
  for (const material of materials) {
    if (!material) {
      continue;
    }

    if (typeof material.renderOutline === "boolean") {
      // The PMX files intentionally contain MMD toon outlines. For a floating
      // desktop pet, those outlines read as ink marks around every limb and
      // finger, so keep the native texture/material stack but disable only the
      // silhouette pass in the presentation profile.
      material.renderOutline = false;
      material.outlineAlpha = 0;
    }

    const name = String(material.name ?? "").toLowerCase();
    if (isDesktopSkinMaterial(material)) {
      // Apply the same presentation tone to the body's skin atlas and the
      // model's [FACE]Base material. The source atlases have different base
      // colors, so changing only [BODY] leaves the face visibly disconnected
      // on the darker presets. Keep authored textures and toon layers while
      // lowering the PMX ambient term so the tone is not clamped away.
      material.diffuseColor.copyFrom(
        isDesktopFaceBaseMaterial(material)
          ? desktopFaceDiffuse
          : desktopBodyDiffuse);
      material.ambientColor.copyFrom(desktopBodyAmbient);
    }

    if (name === "[face]noseedge" && material.sphereTexture) {
      // FakeEdge is an authored MMD matcap for the nose-edge helper surface.
      // It becomes a dark spot in the desktop-pet presentation, so retain the
      // helper surface but omit only this optional sphere layer.
      material.sphereTexture = null;
    }
  }
}

function applyInitialMaterialVisibility(meshes) {
  for (const mesh of meshes) {
    if (mesh?.material && Number(mesh.material.alpha) <= 0) {
      mesh.isVisible = false;
    }
  }
}

async function loadCharacter(index, url, requestId) {
  if (!runtime) {
    throw new Error("The MMD runtime is still initializing.");
  }

  disposeCurrentModel();

  const loaded = await loadCharacterAsset(index, url);
  state.container = loaded.container;
  state.model = loaded.model;
  state.modelIndex = index;
  state.modelUrl = loaded.url;
  setCameraForModel(loaded.container.meshes);
  startBlink(loaded.metadata.morphs.map((morph, morphIndex) => ({ ...morph, index: morphIndex })));

  await new Promise((resolve) => requestAnimationFrame(resolve));
  post("model-loaded", { ...describeLoadedCharacter(loaded), requestId });
}

async function loadMotion(name, url, loop = false, requestId) {
  if (!runtime || !vmdLoader || !state.model) {
    throw new Error("Load a character before loading a motion.");
  }

  state.loopCurrentMotion = false;
  state.loopEndFrame = 0;
  state.userPauseRequested = false;
  runtime.pauseAnimation();
  const loadedMotion = await loadCachedMotion(name, url);
  const { motion, rootOffset, visibilityTrackNormalized } = loadedMotion;
  if (state.motionHandle) {
    state.model.destroyRuntimeAnimation(state.motionHandle);
  }

  state.motionHandle = state.model.createRuntimeAnimation(motion);
  state.model.setRuntimeAnimation(state.motionHandle);
  runtime.setManualAnimationDuration(motion.endFrame);
  await runtime.seekAnimation(0, true);
  state.loopEndFrame = motion.endFrame;
  state.loopCurrentMotion = Boolean(loop);
  post("motion-loaded", {
    name,
    url: normalizeModelUrl(url),
    endFrame: motion.endFrame,
    boneTracks: motion.boneTracks.length,
    morphTracks: motion.morphTracks.length,
    loop: state.loopCurrentMotion,
    rootOffset,
    visibilityTrackNormalized,
    requestId,
  });
}

async function loadDualCharacters(primaryIndex, primaryUrl, secondaryIndex, secondaryUrl, requestId) {
  if (!runtime) {
    throw new Error("The MMD runtime is still initializing.");
  }

  disposeCurrentModel();

  const primary = await loadCharacterAsset(primaryIndex, primaryUrl);
  let secondary;
  try {
    secondary = await loadCharacterAsset(secondaryIndex, secondaryUrl);
  } catch (error) {
    disposeLoadedCharacter(primary);
    throw error;
  }

  // The Womanizer VMD contains two authored roles. Keep the two models in a
  // compact side-by-side arrangement because there is no MMD stage camera in
  // the floating-pet presentation.
  primary.rootMesh.position.x = -2.05;
  secondary.rootMesh.position.x = 2.05;

  state.container = primary.container;
  state.model = primary.model;
  state.secondaryContainer = secondary.container;
  state.secondaryModel = secondary.model;
  state.modelIndex = primaryIndex;
  state.secondaryModelIndex = secondaryIndex;
  state.modelUrl = primary.url;
  state.secondaryModelUrl = secondary.url;
  // The dance leans both performers outward during its first phrase. Keep a
  // little more camera margin than the static model bounds require so hands
  // and feet remain inside the portrait desktop-pet canvas while dancing.
  setCameraForModel([...primary.container.meshes, ...secondary.container.meshes], 1.72);
  startBlink(primary.metadata.morphs.map((morph, morphIndex) => ({ ...morph, index: morphIndex })));

  await new Promise((resolve) => requestAnimationFrame(resolve));
  post("dual-models-loaded", {
    primary: describeLoadedCharacter(primary),
    secondary: describeLoadedCharacter(secondary),
    requestId,
  });
}

async function loadSecondaryCharacter(primaryIndex, secondaryIndex, secondaryUrl, requestId) {
  if (!runtime) {
    throw new Error("The MMD runtime is still initializing.");
  }
  if (!state.model || !state.container || state.modelIndex !== primaryIndex) {
    throw new Error("The requested primary character is not loaded.");
  }

  const normalizedSecondaryUrl = normalizeModelUrl(secondaryUrl);
  if (state.secondaryModel && state.secondaryModelIndex === secondaryIndex
      && state.secondaryModelUrl === normalizedSecondaryUrl) {
    const primaryRootMesh = state.container.meshes.find((mesh) => MmdMesh.isMmdSkinnedMesh(mesh));
    if (primaryRootMesh) {
      primaryRootMesh.position.x = -2.05;
    }
    const secondaryRootMesh = state.secondaryContainer?.meshes.find(
      (mesh) => MmdMesh.isMmdSkinnedMesh(mesh));
    if (secondaryRootMesh) {
      secondaryRootMesh.position.x = 2.05;
    }
    setCameraForModel([
      ...state.container.meshes,
      ...(state.secondaryContainer?.meshes ?? []),
    ], 1.72);
    post("dual-models-loaded", {
      primary: describeActiveCharacter(state.modelIndex, state.modelUrl, state.container, state.model),
      secondary: describeActiveCharacter(
        state.secondaryModelIndex,
        state.secondaryModelUrl,
        state.secondaryContainer,
        state.secondaryModel,
      ),
      requestId,
    });
    return;
  }

  runtime.pauseAnimation();
  const primaryModelAtStart = state.model;
  const primaryContainerAtStart = state.container;
  const preloadedMatches = state.preloadedSecondary
    && state.preloadedSecondary.index === secondaryIndex
    && state.preloadedSecondary.url === normalizedSecondaryUrl;
  const preloadInProgress = state.secondaryPreloadPromise
    && state.secondaryPreloadIndex === secondaryIndex
    && state.secondaryPreloadUrl === normalizedSecondaryUrl;
  let secondary = preloadedMatches
    ? state.preloadedSecondary
    : preloadInProgress
      ? await state.secondaryPreloadPromise
      : await loadCharacterAsset(secondaryIndex, normalizedSecondaryUrl);
  if (!secondary) {
    throw new Error("The secondary character preload did not produce a model.");
  }
  try {
    if (state.model !== primaryModelAtStart || state.container !== primaryContainerAtStart) {
      throw new Error("The active primary character changed while loading the secondary character.");
    }

    const primaryRootMesh = state.container.meshes.find((mesh) => MmdMesh.isMmdSkinnedMesh(mesh));
    if (!primaryRootMesh) {
      throw new Error("The loaded primary character has no MMD skinned mesh.");
    }

    primaryRootMesh.position.x = -2.05;
    secondary.rootMesh.position.x = 2.05;
    if (state.preloadedSecondary && state.preloadedSecondary !== secondary) {
      disposePreloadedSecondary();
    }
    if (state.preloadedSecondary === secondary) {
      state.preloadedSecondary = null;
    }
    revealPreloadedCharacter(secondary);
    disposeSecondaryCharacter();
    state.secondaryContainer = secondary.container;
    state.secondaryModel = secondary.model;
    state.secondaryModelIndex = secondaryIndex;
    state.secondaryModelUrl = secondary.url;
    setCameraForModel([
      ...state.container.meshes,
      ...state.secondaryContainer.meshes,
    ], 1.72);

    await new Promise((resolve) => requestAnimationFrame(resolve));
    post("dual-models-loaded", {
      primary: describeActiveCharacter(state.modelIndex, state.modelUrl, state.container, state.model),
      secondary: describeLoadedCharacter(secondary),
      requestId,
    });
  } catch (error) {
    disposeLoadedCharacter(secondary);
    throw error;
  }
}

async function preloadSecondaryCharacter(primaryIndex, secondaryIndex, secondaryUrl) {
  if (!runtime || !state.model || !state.container || state.modelIndex !== primaryIndex || state.secondaryModel) {
    return;
  }

  const normalizedSecondaryUrl = normalizeModelUrl(secondaryUrl);
  if (state.preloadedSecondary
      && state.preloadedSecondary.index === secondaryIndex
      && state.preloadedSecondary.url === normalizedSecondaryUrl) {
    return;
  }

  if (state.secondaryPreloadPromise
      && state.secondaryPreloadIndex === secondaryIndex
      && state.secondaryPreloadUrl === normalizedSecondaryUrl) {
    await state.secondaryPreloadPromise;
    return;
  }

  const primaryModelAtStart = state.model;
  const primaryContainerAtStart = state.container;
  const preloadPromise = loadCharacterAsset(secondaryIndex, normalizedSecondaryUrl)
    .then((loaded) => {
      if (state.model !== primaryModelAtStart
          || state.container !== primaryContainerAtStart
          || state.secondaryModel) {
        disposeLoadedCharacter(loaded);
        return null;
      }

      hidePreloadedCharacter(loaded);
      state.preloadedSecondary = loaded;
      return loaded;
    });
  state.secondaryPreloadPromise = preloadPromise;
  state.secondaryPreloadIndex = secondaryIndex;
  state.secondaryPreloadUrl = normalizedSecondaryUrl;
  try {
    await preloadPromise;
  } finally {
    if (state.secondaryPreloadPromise === preloadPromise) {
      state.secondaryPreloadPromise = null;
      state.secondaryPreloadIndex = -1;
      state.secondaryPreloadUrl = "";
    }
  }
}

async function loadDualMotion(name, primaryUrl, secondaryUrl, startFrame = 0, requestId) {
  if (!runtime || !vmdLoader || !state.model || !state.secondaryModel) {
    throw new Error("Load the two characters before loading a dual motion.");
  }

  state.loopCurrentMotion = false;
  state.loopEndFrame = 0;
  state.userPauseRequested = false;
  runtime.pauseAnimation();
  const primaryModelAtStart = state.model;
  const secondaryModelAtStart = state.secondaryModel;
  const [primaryLoaded, secondaryLoaded] = await Promise.all([
    loadCachedMotion(`${name}-primary`, primaryUrl),
    loadCachedMotion(`${name}-secondary`, secondaryUrl),
  ]);
  const {
    motion: primaryMotion,
    rootOffset: primaryRootOffset,
    visibilityTrackNormalized: primaryVisibilityTrackNormalized,
  } = primaryLoaded;
  const {
    motion: secondaryMotion,
    rootOffset: secondaryRootOffset,
    visibilityTrackNormalized: secondaryVisibilityTrackNormalized,
  } = secondaryLoaded;

  if (state.model !== primaryModelAtStart || state.secondaryModel !== secondaryModelAtStart) {
    throw new Error("The active characters changed while loading the dual motion.");
  }

  if (state.motionHandle) {
    state.model.destroyRuntimeAnimation(state.motionHandle);
  }
  if (state.secondaryMotionHandle) {
    state.secondaryModel.destroyRuntimeAnimation(state.secondaryMotionHandle);
  }

  state.motionHandle = state.model.createRuntimeAnimation(primaryMotion);
  state.model.setRuntimeAnimation(state.motionHandle);
  state.secondaryMotionHandle = state.secondaryModel.createRuntimeAnimation(secondaryMotion);
  state.secondaryModel.setRuntimeAnimation(state.secondaryMotionHandle);
  const duration = Math.max(primaryMotion.endFrame, secondaryMotion.endFrame);
  const requestedStartFrame = Number.isFinite(Number(startFrame))
    ? Math.max(0, Math.floor(Number(startFrame)))
    : 0;
  const playbackStartFrame = Math.min(
    requestedStartFrame,
    Math.min(primaryMotion.endFrame, secondaryMotion.endFrame),
  );
  runtime.setManualAnimationDuration(duration);
  await runtime.seekAnimation(playbackStartFrame, true);
  post("dual-motion-loaded", {
    name,
    primaryUrl: normalizeModelUrl(primaryUrl),
    secondaryUrl: normalizeModelUrl(secondaryUrl),
    primaryEndFrame: primaryMotion.endFrame,
    secondaryEndFrame: secondaryMotion.endFrame,
    startFrame: playbackStartFrame,
    primaryBoneTracks: primaryMotion.boneTracks.length,
    secondaryBoneTracks: secondaryMotion.boneTracks.length,
    primaryMorphTracks: primaryMotion.morphTracks.length,
    secondaryMorphTracks: secondaryMotion.morphTracks.length,
    loop: false,
    primaryRootOffset,
    secondaryRootOffset,
    primaryVisibilityTrackNormalized,
    secondaryVisibilityTrackNormalized,
    requestId,
  });
}

async function loadCachedMotion(name, url) {
  const normalizedUrl = normalizeModelUrl(url);
  const cached = state.motionCache.get(normalizedUrl);
  if (cached) {
    return cached;
  }

  const pending = state.motionLoadPromises.get(normalizedUrl);
  if (pending) {
    return pending;
  }

  const loadPromise = vmdLoader.loadAsync(name, normalizedUrl).then((motion) => {
    const loaded = {
      motion,
      rootOffset: normalizeMotionRoot(motion),
      visibilityTrackNormalized: normalizeMotionVisibility(motion),
    };
    state.motionCache.set(normalizedUrl, loaded);
    return loaded;
  }).finally(() => {
    state.motionLoadPromises.delete(normalizedUrl);
  });
  state.motionLoadPromises.set(normalizedUrl, loadPromise);
  return loadPromise;
}

async function preloadDualMotion(name, primaryUrl, secondaryUrl) {
  if (!vmdLoader) {
    throw new Error("The VMD loader is still initializing.");
  }

  await Promise.all([
    loadCachedMotion(`${name}-primary`, primaryUrl),
    loadCachedMotion(`${name}-secondary`, secondaryUrl),
  ]);
  post("dual-motion-preloaded", { name });
}

async function loadCharacterAsset(index, url) {
  const modelUrl = normalizeModelUrl(url);
  const container = await LoadAssetContainerAsync(modelUrl, scene);
  container.addAllToScene();

  try {
    const rootMesh = container.meshes.find((mesh) => MmdMesh.isMmdSkinnedMesh(mesh));
    if (!rootMesh) {
      throw new Error(`No MMD skinned mesh was found in ${modelUrl}.`);
    }

    const metadata = rootMesh.metadata;
    configureDesktopPetMaterials(container.materials);
    applyInitialMaterialVisibility(container.meshes);
    const model = runtime.createMmdModel(rootMesh, {
      materialProxyConstructor: MmdStandardMaterialProxy,
      buildPhysics: true,
      trimMetadata: false,
    });

    return { index, url: modelUrl, container, rootMesh, model, metadata };
  } catch (error) {
    container.removeAllFromScene();
    container.dispose();
    throw error;
  }
}

function disposeLoadedCharacter(loaded) {
  if (loaded.model && runtime) {
    runtime.destroyMmdModel(loaded.model);
  }
  loaded.container.removeAllFromScene();
  loaded.container.dispose();
}

function hidePreloadedCharacter(loaded) {
  loaded.preloadVisibility = loaded.container.meshes.map((mesh) => mesh.isVisible);
  for (const mesh of loaded.container.meshes) {
    mesh.isVisible = false;
  }
}

function revealPreloadedCharacter(loaded) {
  if (!loaded.preloadVisibility) {
    return;
  }
  loaded.container.meshes.forEach((mesh, index) => {
    mesh.isVisible = loaded.preloadVisibility[index] ?? true;
  });
  loaded.preloadVisibility = null;
}

function disposePreloadedSecondary() {
  if (state.preloadedSecondary) {
    disposeLoadedCharacter(state.preloadedSecondary);
  }
  state.preloadedSecondary = null;
  state.secondaryPreloadPromise = null;
  state.secondaryPreloadIndex = -1;
  state.secondaryPreloadUrl = "";
}

function disposeSecondaryCharacter() {
  if (state.secondaryModel && runtime) {
    runtime.destroyMmdModel(state.secondaryModel);
  }
  if (state.secondaryContainer) {
    state.secondaryContainer.removeAllFromScene();
    state.secondaryContainer.dispose();
  }
  state.secondaryModel = null;
  state.secondaryContainer = null;
  state.secondaryMotionHandle = null;
  state.secondaryModelIndex = -1;
  state.secondaryModelUrl = "";
}

function describeLoadedCharacter(loaded) {
  const { index, url, container, metadata } = loaded;
  return {
    index,
    url,
    modelName: metadata.header.modelName,
    englishModelName: metadata.header.englishModelName,
    meshCount: container.meshes.length,
    materialCount: metadata.materials.length,
    boneCount: metadata.bones.length,
    morphs: describeMorphs(metadata),
    physics: true,
    physicsEngine: "babylon-mmd-wasm-integrated",
    rigidBodyCount: metadata.rigidBodies.length,
    jointCount: metadata.joints.length,
  };
}

function describeActiveCharacter(index, url, container, model) {
  const rootMesh = container?.meshes.find((mesh) => MmdMesh.isMmdSkinnedMesh(mesh));
  if (!rootMesh || !container || !model) {
    throw new Error("The active character is not fully loaded.");
  }
  return describeLoadedCharacter({
    index,
    url,
    container,
    model,
    rootMesh,
    metadata: rootMesh.metadata,
  });
}

function normalizeMotionRoot(motion) {
  const rootNames = new Set(["センター", "センター", "center"]);
  const rootTrack = motion.movableBoneTracks.find((track) => rootNames.has(String(track.name).toLowerCase()));
  if (!rootTrack || rootTrack.positions.length < 3) {
    return null;
  }

  const offset = [rootTrack.positions[0], rootTrack.positions[1], rootTrack.positions[2]];
  // Several multi-person MMD dances move the performer across a stage. A
  // floating pet has no stage or camera animation, so preserve the authored
  // body motion and vertical bounce while pinning stage translation to the
  // center of the pet canvas. This adapts the playback context; it does not
  // invent or rewrite any dance keyframes on disk.
  for (let index = 0; index < rootTrack.positions.length; index += 3) {
    rootTrack.positions[index] = 0;
    rootTrack.positions[index + 1] -= offset[1];
    rootTrack.positions[index + 2] = 0;
  }

  return offset.map((value) => Number(value.toFixed(3)));
}

function normalizeMotionVisibility(motion) {
  const visibilityTrack = motion.propertyTrack?.visibles;
  if (!visibilityTrack || visibilityTrack.length === 0) {
    return false;
  }

  let changed = false;
  for (let index = 0; index < visibilityTrack.length; index += 1) {
    if (visibilityTrack[index] === 0) {
      changed = true;
      visibilityTrack[index] = 1;
    }
  }

  return changed;
}

function resetMorphs() {
  for (const model of activeModels()) {
    model.morph.resetMorphWeights();
    model.morph.update();
  }
  applyDesktopPetMaterialPresentation();
}

window.chrome?.webview?.addEventListener("message", async (event) => {
  const message = event.data ?? {};

  try {
    switch (message.type) {
      case "load-character":
        await loadCharacter(message.index, message.url, message.requestId);
        break;
      case "load-motion":
        await loadMotion(
          message.name ?? "desktop-pet-motion",
          message.url,
          Boolean(message.loop),
          message.requestId,
        );
        break;
      case "load-dual-character":
        await loadDualCharacters(
          Number(message.primaryIndex),
          message.primaryUrl,
          Number(message.secondaryIndex),
          message.secondaryUrl,
          message.requestId,
        );
        break;
      case "load-secondary-character":
        await loadSecondaryCharacter(
          Number(message.primaryIndex),
          Number(message.secondaryIndex),
          message.secondaryUrl,
          message.requestId,
        );
        break;
      case "preload-secondary-character":
        try {
          await preloadSecondaryCharacter(
            Number(message.primaryIndex),
            Number(message.secondaryIndex),
            message.secondaryUrl,
          );
        } catch (error) {
          const exception = error instanceof Error ? error : new Error(String(error));
          // Idle preloading is opportunistic and must never surface as a
          // foreground load failure.
          post("secondary-character-preload-failed", {
            message: exception.message,
          });
        }
        break;
      case "preload-dual-motion":
        try {
          await preloadDualMotion(
            message.name ?? "desktop-pet-dual-motion",
            message.primaryUrl,
            message.secondaryUrl,
          );
        } catch (error) {
          const exception = error instanceof Error ? error : new Error(String(error));
          // Preloading is opportunistic. Do not turn a background cache miss
          // into a failure for the foreground model/motion request.
          post("dual-motion-preload-failed", {
            name: message.name ?? "desktop-pet-dual-motion",
            message: exception.message,
          });
        }
        break;
      case "load-dual-motion":
        await loadDualMotion(
          message.name ?? "desktop-pet-dual-motion",
          message.primaryUrl,
          message.secondaryUrl,
          message.startFrame,
          message.requestId,
        );
        break;
      case "load-music":
        await loadMusic(message.url, message.requestId);
        break;
      case "clear-music":
        await loadMusic("", message.requestId);
        break;
      case "set-skin-tone":
        setDesktopBodyTone(message.tone, message.requestId);
        break;
      case "set-morph":
        const morphResult = applyMorph(Number(message.index), Number(message.weight));
        post("morph-updated", {
          index: Number(message.index),
          weight: Number(message.weight),
          requestId: message.requestId,
          ...morphResult,
        });
        break;
      case "reset-morphs":
        resetMorphs();
        post("morphs-reset", { requestId: message.requestId });
        break;
      case "play":
        state.userPauseRequested = false;
        await runtime.playAnimation();
        break;
      case "pause":
        state.userPauseRequested = true;
        runtime.pauseAnimation();
        break;
      default:
        break;
    }
  } catch (error) {
    const exception = error instanceof Error ? error : new Error(String(error));
    post("runtime-error", {
      operation: message.type,
      message: exception.message,
      stack: exception.stack ?? "",
      requestId: message.requestId,
    });
  }
});

canvas.addEventListener("contextmenu", (event) => event.preventDefault());
canvas.addEventListener("wheel", (event) => event.preventDefault(), { passive: false });

engine.runRenderLoop(() => {
  if (!scene.isDisposed && scene.activeCamera) {
    scene.render();
  }
});

window.addEventListener("resize", () => {
  engine.resize();
  if (state.camera && state.container) {
    setCameraForModel(activeContainers().flatMap((container) => container.meshes), state.cameraFitScale);
  }
});

window.HimeMmdRuntime = {
  scene,
  engine,
  runtime,
  model: null,
  loadCharacter,
  loadMotion,
  resetMorphs,
};

initializeMmdRuntime().catch((error) => {
  const exception = error instanceof Error ? error : new Error(String(error));
  post("runtime-error", {
    operation: "initialize-runtime",
    message: exception.message,
    stack: exception.stack ?? "",
  });
});
