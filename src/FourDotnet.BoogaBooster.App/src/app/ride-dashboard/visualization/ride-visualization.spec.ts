import { TestBed } from '@angular/core/testing';
import * as THREE from 'three';

import { LIGHT_RATINGS, fixtureLight } from '../models/ride.models';
import { createTelemetry } from '../testing/fake-ride-telemetry-source';
import {
  LIGHT_EMISSIVE_COLOR,
  RideVisualization,
  RigLightFrame,
  angularVelocity,
  applyRigLights,
  buildRig,
  createRigLightMaterials,
  podFreeAngle,
  rpmToRadPerSec,
} from './ride-visualization';

/** A dark rig frame (every fixture at 0 lm), from the at-rest fixture telemetry. */
function darkFrame(): RigLightFrame {
  const telemetry = createTelemetry();
  return { mill: telemetry.mill, hubs: telemetry.hubs, gondolas: telemetry.gondolas };
}

/** Whether `node` hangs (directly or indirectly) under `ancestor`. */
function isUnder(node: THREE.Object3D, ancestor: THREE.Object3D): boolean {
  for (let current = node.parent; current; current = current.parent) {
    if (current === ancestor) {
      return true;
    }
  }
  return false;
}

describe('RideVisualization', () => {
  function render() {
    const fixture = TestBed.createComponent(RideVisualization);
    fixture.componentRef.setInput('millSpeedRpm', 6);
    fixture.componentRef.setInput('millDirection', 'forward');
    fixture.componentRef.setInput('hubSpeedRpm', 9);
    fixture.componentRef.setInput('hubDirection', 'reverse');
    fixture.componentRef.setInput('gondolaBrakeEngaged', false);
    fixture.componentRef.setInput('lights', darkFrame());
    fixture.detectChanges();
    return fixture;
  }

  it('mounts without a WebGL context and stays hidden from assistive tech', () => {
    const fixture = render();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.getAttribute('aria-hidden')).toBe('true');
    expect(host.querySelector('.viz')).not.toBeNull();
  });

  it('maps rpm to radians per second', () => {
    expect(rpmToRadPerSec(60)).toBeCloseTo(2 * Math.PI, 5);
    expect(rpmToRadPerSec(0)).toBe(0);
  });

  it('derives angular velocity from signed rpm', () => {
    expect(angularVelocity(60)).toBeCloseTo(2 * Math.PI, 5);
    expect(angularVelocity(-60)).toBeCloseTo(-2 * Math.PI, 5);
    expect(angularVelocity(0)).toBe(0);
  });

  it('coerces a non-finite rpm to 0 instead of corrupting the rotation', () => {
    expect(angularVelocity(NaN)).toBe(0);
    expect(angularVelocity(Infinity)).toBe(0);
    expect(angularVelocity(-Infinity)).toBe(0);
  });

  it('advances the free pod trajectory with the combined rotation', () => {
    // At rest the pod sits at its phase-shifted swing baseline.
    expect(podFreeAngle(0, 0)).toBe(0);
    expect(podFreeAngle(0, Math.PI / 2)).toBeCloseTo(1, 5);
    // The trajectory counter-rotates against the combined spin.
    expect(podFreeAngle(Math.PI, 0)).toBeCloseTo(-Math.PI, 5);
  });
});

describe('ride lights in the visualization', () => {
  it('gives each of the 41 fixtures its own dark, warm-white emissive material', () => {
    const materials = createRigLightMaterials();
    const all = [
      materials.crown,
      ...materials.millArms,
      ...materials.hubs,
      ...materials.hubArms.flat(),
      ...materials.gondolas.flat(),
    ];

    expect(all).toHaveLength(41);
    expect(new Set(all).size).toBe(41);
    all.forEach((material) => {
      expect(material.emissive.getHex()).toBe(LIGHT_EMISSIVE_COLOR);
      expect(material.emissiveIntensity).toBe(0);
    });
  });

  it('glows a gondola at its rating fully and a dark gondola not at all', () => {
    const materials = createRigLightMaterials();
    const frame = darkFrame();
    const gondolas = frame.gondolas.map((gondola) =>
      gondola.id === 6 ? { ...gondola, light: fixtureLight(1000, LIGHT_RATINGS.gondola) } : gondola,
    );

    applyRigLights(materials, { ...frame, gondolas });

    // Gondola id 6 is hub 1, gondola 1.
    expect(materials.gondolas[1][1].emissiveIntensity).toBe(1);
    expect(materials.gondolas[1][2].emissiveIntensity).toBe(0);
    expect(materials.gondolas[0][0].emissiveIntensity).toBe(0);
  });

  it('follows every fixture of a frame at its level', () => {
    const materials = createRigLightMaterials();
    const frame = darkFrame();

    applyRigLights(materials, {
      mill: {
        crownLight: fixtureLight(2500, LIGHT_RATINGS.millCrown),
        armLights: [400, 800, 1200, 1600].map((l) => fixtureLight(l, LIGHT_RATINGS.millArm)),
      },
      hubs: frame.hubs.map((hub, h) => ({
        light: fixtureLight(250 * (h + 1), LIGHT_RATINGS.hub),
        armLights: [0, 1, 2, 3].map((j) => fixtureLight(250 * j, LIGHT_RATINGS.hubArm)),
      })),
      gondolas: frame.gondolas.map((gondola) => ({
        ...gondola,
        light: fixtureLight(gondola.id * 50, LIGHT_RATINGS.gondola),
      })),
    });

    expect(materials.crown.emissiveIntensity).toBe(0.5);
    expect(materials.millArms.map((m) => m.emissiveIntensity)).toEqual([0.1, 0.2, 0.3, 0.4]);
    expect(materials.hubs.map((m) => m.emissiveIntensity)).toEqual([0.1, 0.2, 0.3, 0.4]);
    expect(materials.hubArms[2].map((m) => m.emissiveIntensity)).toEqual([0, 0.1, 0.2, 0.3]);
    expect(materials.gondolas[3][3].emissiveIntensity).toBeCloseTo(0.8, 12);
  });

  it('never glows from a corrupt or missing level', () => {
    const materials = createRigLightMaterials();
    const frame = darkFrame();

    applyRigLights(materials, {
      mill: { crownLight: { lumens: 0, level: Number.NaN }, armLights: [] },
      hubs: [],
      gondolas: frame.gondolas.map((gondola) => ({ ...gondola, light: { lumens: 0, level: 7 } })),
    });

    expect(materials.crown.emissiveIntensity).toBe(0);
    expect(materials.millArms[0].emissiveIntensity).toBe(0);
    expect(materials.hubs[0].emissiveIntensity).toBe(0);
    expect(materials.gondolas[0][0].emissiveIntensity).toBe(1);
  });

  it('mounts each light on the part it belongs to, so it turns with that part', () => {
    const materials = createRigLightMaterials();
    const rig = buildRig(materials);

    expect(rig.fixtures.crown.material).toBe(materials.crown);
    expect(rig.fixtures.crown.parent).toBe(rig.mainPivot);

    for (let h = 0; h < 4; h++) {
      const hubPivot = rig.hubPivots[h];
      expect(rig.fixtures.millArms[h].material).toBe(materials.millArms[h]);
      expect(rig.fixtures.millArms[h].parent).toBe(hubPivot.parent);
      expect(rig.fixtures.hubs[h].material).toBe(materials.hubs[h]);
      expect(rig.fixtures.hubs[h].parent).toBe(hubPivot);

      for (let j = 0; j < 4; j++) {
        const gondolaNode = rig.gondolas[h * 4 + j].obj;
        expect(rig.fixtures.hubArms[h][j].material).toBe(materials.hubArms[h][j]);
        expect(isUnder(rig.fixtures.hubArms[h][j], hubPivot)).toBe(true);
        expect(rig.fixtures.gondolas[h][j].material).toBe(materials.gondolas[h][j]);
        expect(rig.fixtures.gondolas[h][j].parent).toBe(gondolaNode);
        expect(isUnder(gondolaNode, hubPivot)).toBe(true);
      }
    }
  });
});
