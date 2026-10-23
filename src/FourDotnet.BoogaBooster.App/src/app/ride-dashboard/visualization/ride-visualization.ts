import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  input,
  viewChild,
} from '@angular/core';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

import {
  GONDOLAS_PER_HUB,
  Gondola,
  HUB_COUNT,
  Hub,
  Mill,
  MotorDirection,
} from '../models/ride.models';

/** Ride geometry (metres) — mirrors the reference model's parameter set. */
const P = {
  towerRadius: 0.6,
  towerHeight: 6.0,
  baseSize: 3.4,
  baseHeight: 0.35,
  mainMotorR: 1.05,
  mainMotorH: 1.3,
  mainArmLen: 6.0,
  mainArmW: 0.42,
  mainArmH: 0.42,
  hubR: 0.62,
  hubH: 1.0,
  hubArmLen: 2.0,
  hubArmW: 0.26,
  hubArmH: 0.26,
  gondolaLen: 2.0,
  gondolaW: 0.85,
  gondolaH: 1.15,
  gondolaDrop: 0.55,
};

/** Height of the arm plane above the ground. */
const ARM_HEIGHT = P.towerHeight;

/** Largest simulation step applied per frame, in seconds (guards tab-switch jumps). */
const MAX_STEP_S = 0.05;

/** Converts revolutions per minute to radians per second. */
export function rpmToRadPerSec(rpm: number): number {
  return (rpm / 60) * Math.PI * 2;
}

/**
 * Coerces a non-finite number (`NaN`/`±Infinity`) to `0`; otherwise returns it
 * unchanged. A missing or corrupt telemetry frame must never inject a
 * non-finite value into the scene graph — that silently blanks the whole
 * rendered rig, since Three.js matrices built from `NaN` propagate to every
 * descendant transform.
 */
function finiteOrZero(value: number): number {
  return Number.isFinite(value) ? value : 0;
}

/**
 * Signed angular velocity (rad/s) from a motor's sensed speed. The telemetry
 * `rpm` is already signed — negative while the body physically turns in reverse
 * — so the rotation direction is carried by the sign of `rpm` alone and the
 * commanded direction must NOT be applied again (doing so would cancel out a
 * reversal). A non-finite `rpm` (e.g. an absent/malformed telemetry frame) is
 * treated as stopped rather than corrupting the loop's accumulated rotation.
 */
export function angularVelocity(rpm: number): number {
  return rpmToRadPerSec(finiteOrZero(rpm));
}

/**
 * Free-running pod angle (radians) for the accumulated combined rotation and a
 * pod's phase offset: a counter-rotation against the combined spin plus a
 * phase-shifted swing. This is the trajectory a pod follows while its brake is
 * released. The render loop applies only this angle's per-frame change, and
 * skips it while the brake is engaged, so a braked pod holds its current
 * rotation instead of snapping back to the hub-arm alignment.
 */
export function podFreeAngle(comboA: number, phase: number): number {
  return -comboA + Math.sin(comboA + phase);
}

/** Warm white the light fixtures glow in. */
export const LIGHT_EMISSIVE_COLOR = 0xffd9a0;

/** The rig colour of each fixture-carrying part. */
const PART_COLORS = {
  crown: 0x8d9096,
  millArm: 0xe6b826,
  hub: 0x347ad9,
  hubArm: 0xf2d959,
  gondola: 0x27b374,
} as const;

/** The light output the visualization renders: one frame of the rig's 41 fixtures. */
export interface RigLightFrame {
  readonly mill: Pick<Mill, 'crownLight' | 'armLights'>;
  readonly hubs: readonly Pick<Hub, 'light' | 'armLights'>[];
  readonly gondolas: readonly Pick<Gondola, 'id' | 'light'>[];
}

/**
 * One material per light fixture, so each fixture can glow on its own. Hub
 * arms and gondolas are indexed `[hub][gondola on hub]`.
 */
export interface RigLightMaterials {
  readonly crown: THREE.MeshStandardMaterial;
  readonly millArms: readonly THREE.MeshStandardMaterial[];
  readonly hubs: readonly THREE.MeshStandardMaterial[];
  readonly hubArms: readonly (readonly THREE.MeshStandardMaterial[])[];
  readonly gondolas: readonly (readonly THREE.MeshStandardMaterial[])[];
}

/** A part material that can glow: its rig colour plus a dark warm-white emissive. */
function fixtureMaterial(color: number): THREE.MeshStandardMaterial {
  return new THREE.MeshStandardMaterial({
    color,
    roughness: 0.55,
    metalness: 0.12,
    emissive: LIGHT_EMISSIVE_COLOR,
    emissiveIntensity: 0,
  });
}

/** A fresh, dark material for each of the rig's 41 fixtures. */
export function createRigLightMaterials(): RigLightMaterials {
  const perHub = (color: number) =>
    Array.from({ length: HUB_COUNT }, () =>
      Array.from({ length: GONDOLAS_PER_HUB }, () => fixtureMaterial(color)),
    );
  return {
    crown: fixtureMaterial(PART_COLORS.crown),
    millArms: Array.from({ length: HUB_COUNT }, () => fixtureMaterial(PART_COLORS.millArm)),
    hubs: Array.from({ length: HUB_COUNT }, () => fixtureMaterial(PART_COLORS.hub)),
    hubArms: perHub(PART_COLORS.hubArm),
    gondolas: perHub(PART_COLORS.gondola),
  };
}

/** A fixture's glow strength: its level, guarded against a bad frame and clamped to `[0, 1]`. */
function glow(level: number | undefined): number {
  return Math.min(1, Math.max(0, finiteOrZero(level ?? 0)));
}

/**
 * Sets every fixture material's glow to its fixture's level in `frame`: dark
 * at 0 lm, full brightness at its rating. Gondolas are placed by their id
 * (`hub * 4 + gondola + 1`), so the frame's gondola order doesn't matter.
 */
export function applyRigLights(materials: RigLightMaterials, frame: RigLightFrame): void {
  materials.crown.emissiveIntensity = glow(frame.mill.crownLight.level);
  materials.millArms.forEach((material, h) => {
    material.emissiveIntensity = glow(frame.mill.armLights[h]?.level);
  });
  materials.hubs.forEach((material, h) => {
    material.emissiveIntensity = glow(frame.hubs[h]?.light.level);
    materials.hubArms[h].forEach((arm, j) => {
      arm.emissiveIntensity = glow(frame.hubs[h]?.armLights[j]?.level);
    });
  });
  for (const gondola of frame.gondolas) {
    const index = gondola.id - 1;
    const material =
      materials.gondolas[Math.floor(index / GONDOLAS_PER_HUB)]?.[index % GONDOLAS_PER_HUB];
    if (material) {
      material.emissiveIntensity = glow(gondola.light.level);
    }
  }
}

export interface GondolaNode {
  readonly obj: THREE.Object3D;
  readonly phase: number;
  /**
   * The pod's free-trajectory angle sampled on the previous frame. Drives the
   * per-frame delta applied while the brake is released, and is refreshed every
   * frame (braked or not) so releasing the brake resumes from the held position
   * without a jump.
   */
  prevFree: number;
}

/**
 * Central visualization: a live 3D render of the ride — one central mill turning
 * four arms, each carrying a hub that spins four gondolas. The mill and hubs turn
 * at the sensed speed and direction from the operation controls (both can run
 * either way), and the gondola brake holds or frees the pods. Every light
 * fixture — the mill crown, mill arms, hubs, hub arms and gondolas — glows with
 * its fixture's reported output.
 *
 * Everything shown is duplicated in the text panels, so the canvas is decorative
 * (`aria-hidden`). WebGL setup is guarded so the component degrades to an empty,
 * harmless element where WebGL is unavailable (e.g. jsdom under test).
 */
@Component({
  selector: 'bb-ride-visualization',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  template: `<div #host class="viz"></div>`,
  styles: `
    :host {
      display: block;
      width: 100%;
      height: 100%;
    }
    .viz {
      width: 100%;
      height: 100%;
    }
    .viz canvas {
      display: block;
      width: 100%;
      height: 100%;
      border-radius: 0.5rem;
    }
  `,
})
export class RideVisualization {
  readonly millSpeedRpm = input.required<number>();
  readonly millDirection = input.required<MotorDirection>();
  readonly hubSpeedRpm = input.required<number>();
  readonly hubDirection = input.required<MotorDirection>();
  readonly gondolaBrakeEngaged = input.required<boolean>();
  /** The latest output of every light fixture on the rig. */
  readonly lights = input.required<RigLightFrame>();

  private readonly hostRef = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private readonly destroyRef = inject(DestroyRef);

  private renderer?: THREE.WebGLRenderer;

  constructor() {
    afterNextRender(() => this.initScene());
  }

  private initScene(): void {
    const host = this.hostRef().nativeElement;

    let renderer: THREE.WebGLRenderer;
    try {
      renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    } catch {
      // No WebGL (e.g. jsdom during tests). Leave the element empty and harmless.
      return;
    }
    this.renderer = renderer;

    const width = host.clientWidth || 640;
    const height = host.clientHeight || 480;
    const pixelRatio = typeof devicePixelRatio === 'number' ? Math.min(devicePixelRatio, 2) : 1;
    renderer.setPixelRatio(pixelRatio);
    renderer.setSize(width, height, false);
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    host.appendChild(renderer.domElement);

    // The canvas is created by three.js, not by the Angular template, so the
    // component's emulated-encapsulation styles (`.viz canvas { … }`) never match
    // it. Size it explicitly so it always fills the host and displays correctly on
    // HiDPI screens regardless of the (device-pixel-scaled) drawing-buffer size.
    renderer.domElement.style.display = 'block';
    renderer.domElement.style.width = '100%';
    renderer.domElement.style.height = '100%';

    const scene = new THREE.Scene();
    scene.fog = new THREE.Fog(0x0f1420, 40, 95);

    const camera = new THREE.PerspectiveCamera(48, width / height, 0.1, 1000);
    camera.position.set(18, 13, 18);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.target.set(0, 5.5, 0);

    this.addLighting(scene);
    this.addGround(scene);
    this.addStaticStructure(scene);
    const lightMaterials = createRigLightMaterials();
    const { mainPivot, hubPivots, gondolas } = buildRig(lightMaterials);
    scene.add(mainPivot);

    const reducedMotion =
      typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

    const clock = new THREE.Clock();
    let mainA = 0;
    let hubA = 0;
    let comboA = 0;

    renderer.setAnimationLoop(() => {
      const dt = finiteOrZero(Math.min(clock.getDelta(), MAX_STEP_S));
      if (!reducedMotion) {
        const wm = angularVelocity(this.millSpeedRpm());
        const wh = angularVelocity(this.hubSpeedRpm());
        // `angularVelocity` already guards `rpm`, but the accumulators are
        // re-guarded here too so a bad frame can never leave `rotation.y`
        // non-finite, however it might arise.
        mainA = finiteOrZero(mainA + wm * dt);
        hubA = finiteOrZero(hubA + wh * dt);
        comboA = finiteOrZero(comboA + (wm + wh) * dt);
      }

      const braked = this.gondolaBrakeEngaged();
      mainPivot.rotation.y = mainA;
      for (const hub of hubPivots) {
        hub.rotation.y = hubA;
      }
      for (const gondola of gondolas) {
        // The pods' free trajectory keeps advancing with the combined rotation.
        // Apply only its change since the previous frame, and only while the
        // brake is released — so an engaged brake holds each pod exactly where
        // it is rather than snapping it back to the hub-arm alignment.
        const freeAngle = podFreeAngle(comboA, gondola.phase);
        const delta = freeAngle - gondola.prevFree;
        gondola.prevFree = freeAngle;
        if (!braked) {
          gondola.obj.rotation.y = finiteOrZero(gondola.obj.rotation.y + delta);
        }
      }

      // Lights follow the latest telemetry frame, not the animation clock, so
      // they still update under reduced motion.
      applyRigLights(lightMaterials, this.lights());

      controls.update();
      renderer.render(scene, camera);
    });

    // Resizing is deferred to the next animation frame and skipped when the size
    // hasn't changed. Doing this work synchronously inside the observer callback is
    // what provokes the browser's "ResizeObserver loop completed with undelivered
    // notifications" error — which, under the high-rate telemetry re-renders, floods
    // the global error handler and can destabilize the canvas.
    let lastWidth = width;
    let lastHeight = height;
    let resizeHandle = 0;
    const applyResize = (): void => {
      resizeHandle = 0;
      const w = host.clientWidth || width;
      const h = host.clientHeight || height;
      if (w === lastWidth && h === lastHeight) {
        return;
      }
      lastWidth = w;
      lastHeight = h;
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      renderer.setSize(w, h, false);
    };
    const resize = new ResizeObserver(() => {
      if (resizeHandle === 0) {
        resizeHandle = requestAnimationFrame(applyResize);
      }
    });
    resize.observe(host);

    this.destroyRef.onDestroy(() => {
      if (resizeHandle !== 0) {
        cancelAnimationFrame(resizeHandle);
      }
      resize.disconnect();
      renderer.setAnimationLoop(null);
      controls.dispose();
      this.disposeScene(scene);
      renderer.dispose();
      renderer.domElement.remove();
      this.renderer = undefined;
    });
  }

  private addLighting(scene: THREE.Scene): void {
    scene.add(new THREE.HemisphereLight(0xbfd4ff, 0x2a2620, 1.0));
    const sun = new THREE.DirectionalLight(0xffffff, 2.2);
    sun.position.set(14, 24, 10);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    Object.assign(sun.shadow.camera, {
      left: -20,
      right: 20,
      top: 20,
      bottom: -20,
      near: 1,
      far: 70,
    });
    scene.add(sun);
  }

  private addGround(scene: THREE.Scene): void {
    const ground = new THREE.Mesh(
      new THREE.CircleGeometry(50, 72),
      new THREE.MeshStandardMaterial({ color: 0x232c3d, roughness: 1 }),
    );
    ground.rotation.x = -Math.PI / 2;
    ground.receiveShadow = true;
    scene.add(ground);
  }

  private addStaticStructure(scene: THREE.Scene): void {
    const make = (color: number) =>
      new THREE.MeshStandardMaterial({ color, roughness: 0.55, metalness: 0.12 });

    const base = box(P.baseSize, P.baseHeight, P.baseSize, make(0x47474d));
    base.position.y = P.baseHeight / 2;
    scene.add(base);

    const tower = cyl(P.towerRadius, P.towerHeight, make(0x8d9096));
    tower.position.y = P.towerHeight / 2;
    scene.add(tower);

    const motor = cyl(P.mainMotorR, P.mainMotorH, make(0xcc2e2e));
    motor.position.y = ARM_HEIGHT;
    scene.add(motor);
  }

  private disposeScene(scene: THREE.Scene): void {
    scene.traverse((object) => {
      const mesh = object as Partial<THREE.Mesh>;
      mesh.geometry?.dispose();
      const material = mesh.material;
      if (Array.isArray(material)) {
        material.forEach((m) => m.dispose());
      } else {
        material?.dispose();
      }
    });
  }
}

/** The rotating rig: its pivots, its gondola nodes and the meshes that carry a light. */
export interface Rig {
  readonly mainPivot: THREE.Group;
  readonly hubPivots: readonly THREE.Group[];
  readonly gondolas: GondolaNode[];
  /** The mesh each light fixture is mounted on, mirroring {@link RigLightMaterials}. */
  readonly fixtures: {
    readonly crown: THREE.Mesh;
    readonly millArms: readonly THREE.Mesh[];
    readonly hubs: readonly THREE.Mesh[];
    readonly hubArms: readonly (readonly THREE.Mesh[])[];
    readonly gondolas: readonly (readonly THREE.Mesh[])[];
  };
}

/**
 * Builds the rotating rig — the mill crown and its four arms, each carrying a hub
 * with four arms and gondolas — with every light fixture's mesh using its own
 * material from `lights`. Each fixture mesh hangs under the pivot of the part it
 * belongs to, so its light turns with that part.
 */
export function buildRig(lights: RigLightMaterials): Rig {
  const capMaterial = new THREE.MeshStandardMaterial({
    color: PART_COLORS.crown,
    roughness: 0.55,
    metalness: 0.12,
  });

  const mainPivot = new THREE.Group();
  mainPivot.position.y = ARM_HEIGHT;

  const crown = cyl(P.mainMotorR * 0.6, 0.35, lights.crown);
  crown.position.y = P.mainMotorH / 2 + 0.1;
  mainPivot.add(crown);

  const hubPivots: THREE.Group[] = [];
  const gondolas: GondolaNode[] = [];
  const millArms: THREE.Mesh[] = [];
  const hubs: THREE.Mesh[] = [];
  const hubArms: THREE.Mesh[][] = [];
  const pods: THREE.Mesh[][] = [];

  for (let k = 0; k < HUB_COUNT; k++) {
    const armHinge = new THREE.Group();
    armHinge.rotation.y = (k * Math.PI) / 2;
    mainPivot.add(armHinge);

    const arm = box(P.mainArmLen, P.mainArmH, P.mainArmW, lights.millArms[k]);
    arm.position.x = P.mainArmLen / 2;
    armHinge.add(arm);
    millArms.push(arm);

    const hubPivot = new THREE.Group();
    hubPivot.position.x = P.mainArmLen;
    armHinge.add(hubPivot);
    hubPivots.push(hubPivot);

    const hub = cyl(P.hubR, P.hubH, lights.hubs[k]);
    hubPivot.add(hub);
    hubs.push(hub);
    const hubCap = cyl(P.hubR * 0.6, 0.3, capMaterial);
    hubCap.position.y = P.hubH / 2 + 0.08;
    hubPivot.add(hubCap);

    hubArms.push([]);
    pods.push([]);
    for (let j = 0; j < GONDOLAS_PER_HUB; j++) {
      const hubArmHinge = new THREE.Group();
      hubArmHinge.rotation.y = (j * Math.PI) / 2;
      hubPivot.add(hubArmHinge);

      const hubArm = box(P.hubArmLen, P.hubArmH, P.hubArmW, lights.hubArms[k][j]);
      hubArm.position.x = P.hubArmLen / 2;
      hubArmHinge.add(hubArm);
      hubArms[k].push(hubArm);

      const gondolaPivot = new THREE.Group();
      gondolaPivot.position.x = P.hubArmLen;
      hubArmHinge.add(gondolaPivot);

      const pod = box(P.gondolaLen, P.gondolaH, P.gondolaW, lights.gondolas[k][j]);
      pod.position.y = -P.gondolaDrop;
      gondolaPivot.add(pod);
      pods[k].push(pod);

      gondolas.push({ obj: gondolaPivot, phase: (k * GONDOLAS_PER_HUB + j) * 0.7, prevFree: 0 });
    }
  }

  return {
    mainPivot,
    hubPivots,
    gondolas,
    fixtures: { crown, millArms, hubs, hubArms, gondolas: pods },
  };
}

function box(l: number, h: number, w: number, mat: THREE.Material): THREE.Mesh {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(l, h, w), mat);
  mesh.castShadow = true;
  mesh.receiveShadow = true;
  return mesh;
}

function cyl(r: number, h: number, mat: THREE.Material, segments = 32): THREE.Mesh {
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(r, r, h, segments), mat);
  mesh.castShadow = true;
  return mesh;
}
