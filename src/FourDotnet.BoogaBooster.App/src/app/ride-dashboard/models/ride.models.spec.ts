import {
  GONDOLA_COUNT,
  GONDOLAS_PER_HUB,
  HUB_COUNT,
  HUB_MAX_POWER_WATTS,
  LIGHT_RATINGS,
  LIGHT_RIG_RATED_LUMENS,
  MILL_MAX_POWER_WATTS,
  RideTelemetryGondolaStreamDto,
  RideTelemetryHubStreamDto,
  RideTelemetryStreamDto,
  SEATS_PER_GONDOLA,
  fixtureLight,
  mapRideTelemetry,
  totalLightLumens,
} from './ride.models';

/** A hub DTO with the given index and no load, always forward. */
function hubDto(index: number, powerWatts: number, rpm: number): RideTelemetryHubStreamDto {
  return { index, powerWatts, rpm, direction: 0, loadKg: 0 };
}

/** A gondola DTO with two seats at the given hub/index, brake state and seat data. */
function gondolaDto(
  hubIndex: number,
  index: number,
  overrides: Partial<RideTelemetryGondolaStreamDto> = {},
): RideTelemetryGondolaStreamDto {
  return {
    hubIndex,
    index,
    brake: 0, // Engaged
    angleDegrees: 0,
    rpm: 0,
    lateralG: 0,
    forwardG: 0,
    loadKg: 0,
    isSafeToDispatch: true,
    seats: [
      { position: 0, occupiedKg: 0, restraint: 0, isOccupied: false, isSecured: false },
      { position: 1, occupiedKg: 0, restraint: 0, isOccupied: false, isSecured: false },
    ],
    ...overrides,
  };
}

/** A full, representative telemetry stream DTO; every gondola/hub defaults to at-rest. */
function buildDto(overrides: Partial<RideTelemetryStreamDto> = {}): RideTelemetryStreamDto {
  const hubs = Array.from({ length: HUB_COUNT }, (_, i) => hubDto(i, 0, 0));
  const gondolas = Array.from({ length: GONDOLA_COUNT }, (_, g) =>
    gondolaDto(Math.floor(g / GONDOLAS_PER_HUB), g % GONDOLAS_PER_HUB),
  );
  return {
    state: 0,
    availableTransitions: [1],
    isSafeToStart: true,
    safetyReason: 0,
    simulationTimeSeconds: 0,
    mill: {
      powerWatts: 0,
      rpm: 0,
      direction: 0,
      loadKg: 0,
      passengerLoadKg: 0,
      imbalanceMillimeters: 0,
      isBalanced: true,
      isOverloaded: false,
    },
    hubs,
    gondolas,
    boardedPassengerCount: 0,
    ...overrides,
  };
}

describe('mapRideTelemetry', () => {
  it('maps lifecycle state and available transitions from numeric enums', () => {
    const model = mapRideTelemetry(buildDto({ state: 3, availableTransitions: [4, 6] }));

    expect(model.state).toBe('started');
    expect(model.availableTransitions).toEqual(['stopping', 'emergency-stop']);
  });

  it('converts mill power watts to a percent of the configured maximum, always forward', () => {
    const dto = buildDto();
    const model = mapRideTelemetry({
      ...dto,
      mill: { ...dto.mill, powerWatts: MILL_MAX_POWER_WATTS / 2, rpm: 4.567 },
    });

    expect(model.mill.power).toBe(50);
    expect(model.mill.direction).toBe('forward');
    expect(model.mill.speedRpm).toBe(4.57);
  });

  it('converts each hub power watts to a percent and assigns 1-based ids', () => {
    const dto = buildDto();
    const model = mapRideTelemetry({
      ...dto,
      hubs: [
        hubDto(0, HUB_MAX_POWER_WATTS, 10),
        hubDto(1, HUB_MAX_POWER_WATTS / 4, 5),
        hubDto(2, 0, 0),
        hubDto(3, 0, 0),
      ],
    });

    expect(model.hubs.map((hub) => hub.id)).toEqual([1, 2, 3, 4]);
    expect(model.hubs[0].power).toBe(100);
    expect(model.hubs[1].power).toBe(25);
    expect(model.hubs[0].direction).toBe('forward');
  });

  it('assigns gondola ids from hub index and gondola index, and maps angleDegrees', () => {
    const dto = buildDto();
    const gondolas = dto.gondolas.slice();
    gondolas[0] = gondolaDto(0, 0, { angleDegrees: 12.34 });
    gondolas[5] = gondolaDto(1, 1, { angleDegrees: 90 }); // hub 1, index 1 -> id 6

    const model = mapRideTelemetry({ ...dto, gondolas });

    const first = model.gondolas.find((g) => g.angleDegrees === 12.3);
    expect(first?.id).toBe(1);
    const sixth = model.gondolas.find((g) => g.id === 6);
    expect(sixth?.angleDegrees).toBe(90);
  });

  it('swaps forwardG/lateralG onto vertical/lateral g-force', () => {
    const dto = buildDto();
    const gondolas = dto.gondolas.slice();
    gondolas[0] = gondolaDto(0, 0, { forwardG: 1.23, lateralG: -0.55 });

    const model = mapRideTelemetry({ ...dto, gondolas });

    expect(model.gondolas[0].gForce.vertical).toBe(1.2);
    expect(model.gondolas[0].gForce.lateral).toBe(-0.5);
  });

  it('maps seat state: empty, occupied-unsecured and secured', () => {
    const dto = buildDto();
    const gondolas = dto.gondolas.slice();
    gondolas[0] = gondolaDto(0, 0, {
      seats: [
        // Not occupied regardless of restraint.
        { position: 0, occupiedKg: 0, restraint: 0, isOccupied: false, isSecured: false },
        // Occupied, Closed -> unsecured.
        { position: 1, occupiedKg: 65, restraint: 1, isOccupied: true, isSecured: false },
      ],
    });
    gondolas[1] = gondolaDto(0, 1, {
      seats: [
        {
          position: 'Left',
          occupiedKg: 80,
          restraint: 'Secured',
          isOccupied: true,
          isSecured: true,
        },
        {
          position: 'Right',
          occupiedKg: 0,
          restraint: 'Open',
          isOccupied: false,
          isSecured: false,
        },
      ],
    });

    const model = mapRideTelemetry({ ...dto, gondolas });

    expect(model.gondolas[0].seats).toEqual([
      { id: 1, state: 'empty', occupiedKg: 0 },
      { id: 2, state: 'occupied-unsecured', occupiedKg: 65 },
    ]);
    expect(model.gondolas[1].seats).toEqual([
      { id: 1, state: 'secured', occupiedKg: 80 },
      { id: 2, state: 'empty', occupiedKg: 0 },
    ]);
  });

  it('rounds occupiedKg to the nearest whole kilogram', () => {
    const dto = buildDto();
    const gondolas = dto.gondolas.slice();
    gondolas[0] = gondolaDto(0, 0, {
      seats: [
        { position: 0, occupiedKg: 65.6, restraint: 2, isOccupied: true, isSecured: true },
        { position: 1, occupiedKg: 0, restraint: 0, isOccupied: false, isSecured: false },
      ],
    });

    const model = mapRideTelemetry({ ...dto, gondolas });

    expect(model.gondolas[0].seats[0].occupiedKg).toBe(66);
  });

  it('reports gondolaBrakeEngaged true only when every gondola brake is Engaged (0)', () => {
    const allEngaged = mapRideTelemetry(buildDto());
    expect(allEngaged.gondolaBrakeEngaged).toBe(true);

    const dto = buildDto();
    const gondolas = dto.gondolas.slice();
    gondolas[0] = gondolaDto(0, 0, { brake: 1 }); // Released
    const oneReleased = mapRideTelemetry({ ...dto, gondolas });
    expect(oneReleased.gondolaBrakeEngaged).toBe(false);
  });

  it('accepts string brake values case-insensitively', () => {
    const dto = buildDto();
    const gondolas = dto.gondolas.slice();
    gondolas[0] = gondolaDto(0, 0, { brake: 'engaged' });
    gondolas.forEach((g, i) => {
      if (i !== 0) {
        gondolas[i] = { ...g, brake: 'Engaged' };
      }
    });

    const model = mapRideTelemetry({ ...dto, gondolas });
    expect(model.gondolaBrakeEngaged).toBe(true);
  });

  it('maps brakesEngaged true from the wire field', () => {
    const model = mapRideTelemetry(buildDto({ brakesEngaged: true }));
    expect(model.brakesEngaged).toBe(true);
  });

  it('maps brakesEngaged false from the wire field', () => {
    const model = mapRideTelemetry(buildDto({ brakesEngaged: false }));
    expect(model.brakesEngaged).toBe(false);
  });

  it('defaults brakesEngaged to false when the wire field is absent', () => {
    const dto = buildDto();
    // `buildDto` doesn't set `brakesEngaged`, simulating an older frame.
    const model = mapRideTelemetry(dto);
    expect(model.brakesEngaged).toBe(false);
  });

  it('preserves the gondola and seat counts', () => {
    const model = mapRideTelemetry(buildDto());
    expect(model.gondolas).toHaveLength(GONDOLA_COUNT);
    model.gondolas.forEach((gondola) => expect(gondola.seats).toHaveLength(SEATS_PER_GONDOLA));
  });
});

describe('light rig', () => {
  /** A frame whose rig carries a distinct output on every fixture. */
  function litDto(): RideTelemetryStreamDto {
    const dto = buildDto();
    return {
      ...dto,
      mill: { ...dto.mill, crownLightLumens: 2500, armLightLumens: [400, 800, 1200, 1600] },
      hubs: dto.hubs.map((hub) => ({
        ...hub,
        lightLumens: 1000 + hub.index,
        armLightLumens: [10, 20, 30, 40].map((value) => value + hub.index * 100),
      })),
      gondolas: dto.gondolas.map((gondola) => ({
        ...gondola,
        lightLumens: gondola.hubIndex * 100 + gondola.index * 10 + 5,
      })),
    };
  }

  it('rates the 41 fixtures of the rig at 87 000 lm in total', () => {
    expect(LIGHT_RATINGS).toEqual({
      millCrown: 5000,
      millArm: 4000,
      hub: 2500,
      hubArm: 2500,
      gondola: 1000,
    });
    expect(LIGHT_RIG_RATED_LUMENS).toBe(87000);
  });

  it('maps the mill crown and its arm lights in hub order', () => {
    const model = mapRideTelemetry(litDto());

    expect(model.mill.crownLight).toEqual({ lumens: 2500, level: 0.5 });
    expect(model.mill.armLights.map((light) => light.lumens)).toEqual([400, 800, 1200, 1600]);
    expect(model.mill.armLights.map((light) => light.level)).toEqual([0.1, 0.2, 0.3, 0.4]);
  });

  it('maps each hub light and its arm lights in gondola order', () => {
    const model = mapRideTelemetry(litDto());

    expect(model.hubs[0].light).toEqual({ lumens: 1000, level: 0.4 });
    expect(model.hubs[2].light.lumens).toBe(1002);
    expect(model.hubs[1].armLights.map((light) => light.lumens)).toEqual([110, 120, 130, 140]);
    expect(model.hubs[1].armLights[0].level).toBeCloseTo(110 / 2500, 12);
  });

  it("maps each gondola's own light", () => {
    const model = mapRideTelemetry(litDto());
    // Gondola id = hubIndex * 4 + index + 1, so id 7 is hub 1, gondola 2.
    const gondola = model.gondolas.find((g) => g.id === 7)!;

    expect(gondola.light).toEqual({ lumens: 125, level: 0.125 });
  });

  it('derives every level as lumens over the fixture rating', () => {
    expect(fixtureLight(750, LIGHT_RATINGS.millCrown)).toEqual({ lumens: 750, level: 0.15 });
    expect(fixtureLight(1000, LIGHT_RATINGS.gondola)).toEqual({ lumens: 1000, level: 1 });
    expect(fixtureLight(0, LIGHT_RATINGS.hub)).toEqual({ lumens: 0, level: 0 });
  });

  it('clamps an out-of-range or missing output into the fixture rating', () => {
    expect(fixtureLight(1200, LIGHT_RATINGS.gondola)).toEqual({ lumens: 1000, level: 1 });
    expect(fixtureLight(-5, LIGHT_RATINGS.gondola)).toEqual({ lumens: 0, level: 0 });
    expect(fixtureLight(Number.NaN, LIGHT_RATINGS.gondola)).toEqual({ lumens: 0, level: 0 });
    expect(fixtureLight(undefined, LIGHT_RATINGS.gondola)).toEqual({ lumens: 0, level: 0 });
  });

  it('reads an older frame without light fields as a dark rig of 41 fixtures', () => {
    const model = mapRideTelemetry(buildDto());

    expect(model.mill.crownLight.lumens).toBe(0);
    expect(model.mill.armLights).toHaveLength(HUB_COUNT);
    model.hubs.forEach((hub) => expect(hub.armLights).toHaveLength(GONDOLAS_PER_HUB));
    expect(totalLightLumens(model)).toBe(0);
  });

  it('sums all 41 fixtures into the total output', () => {
    const model = mapRideTelemetry(litDto());

    const mill = 2500 + 400 + 800 + 1200 + 1600;
    const hubs = [0, 1, 2, 3].reduce(
      (total, h) => total + (1000 + h) + (10 + 20 + 30 + 40 + 4 * h * 100),
      0,
    );
    const gondolas = Array.from(
      { length: GONDOLA_COUNT },
      (_, g) => Math.floor(g / GONDOLAS_PER_HUB) * 100 + (g % GONDOLAS_PER_HUB) * 10 + 5,
    ).reduce((total, value) => total + value, 0);

    expect(totalLightLumens(model)).toBe(mill + hubs + gondolas);
  });

  it('totals a rig at full output to its rated 87 000 lm', () => {
    const dto = buildDto();
    const full: RideTelemetryStreamDto = {
      ...dto,
      mill: { ...dto.mill, crownLightLumens: 5000, armLightLumens: [4000, 4000, 4000, 4000] },
      hubs: dto.hubs.map((hub) => ({
        ...hub,
        lightLumens: 2500,
        armLightLumens: [2500, 2500, 2500, 2500],
      })),
      gondolas: dto.gondolas.map((gondola) => ({ ...gondola, lightLumens: 1000 })),
    };

    expect(totalLightLumens(mapRideTelemetry(full))).toBe(LIGHT_RIG_RATED_LUMENS);
  });
});
