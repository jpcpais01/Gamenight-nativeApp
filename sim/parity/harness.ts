// The PWA side of the parity check: plays matches with the PWA's own engine and writes one
// 32-bit hash of the whole state per step (and, on request, the full state at one step).
// Bundled by run.sh with esbuild; `pwa/` is aliased to the PWA repo's src/.
import { writeFileSync } from 'node:fs';
import { Match, type MatchSetup } from 'pwa/sim/match';
import { prepareGroundPasses } from 'pwa/sim/kick';
import { makeInput, type InputState } from 'pwa/sim/input';
import { Drill, type DrillKind } from 'pwa/sim/training';

const store = new Map<string, string>();
(globalThis as any).localStorage = {
  getItem: (k: string) => store.get(k) ?? null,
  setItem: (k: string, v: string) => void store.set(k, v),
  removeItem: (k: string) => void store.delete(k),
};

const PHASES = ['kickoff', 'play', 'out', 'setpiece', 'goal', 'halftime', 'fulltime'];
const ACTIONS = ['none', 'kick', 'tackle', 'slide', 'dive', 'stumble', 'fall', 'header', 'throw', 'catch', 'celebrate', 'stretch'];

const buf = new DataView(new ArrayBuffer(8));
function mix(h: number, x: number): number {
  buf.setFloat64(0, x, true);
  for (let i = 0; i < 8; i++) h = Math.imul(h ^ buf.getUint8(i), 16777619) >>> 0;
  return h;
}

function hash(m: Match): number {
  let h = 2166136261;
  const b = m.ball;
  h = mix(h, m.time);
  h = mix(h, PHASES.indexOf(m.phase));
  h = mix(h, (m.rng as any).s);
  for (const v of [b.pos, b.vel, b.spin]) (h = mix(h, v.x)), (h = mix(h, v.y)), (h = mix(h, v.z));
  h = mix(h, m.owner ? m.owner.id : -1);
  h = mix(h, m.heldBy ? m.heldBy.id : -1);
  h = mix(h, m.controlled.id);
  h = mix(h, m.teams[0].score * 100 + m.teams[1].score);
  for (const p of m.all) {
    h = mix(h, p.pos.x);
    h = mix(h, p.pos.z);
    h = mix(h, p.vel.x);
    h = mix(h, p.vel.z);
    h = mix(h, p.facing);
    h = mix(h, ACTIONS.indexOf(p.action));
    h = mix(h, p.actionT);
    h = mix(h, p.stamina);
  }
  return h;
}

function dump(m: Match): unknown {
  const v = (q: { x: number; y: number; z: number }) => [q.x, q.y, q.z];
  return {
    time: m.time, phase: m.phase, rng: (m.rng as any).s, score: [m.teams[0].score, m.teams[1].score],
    ball: { pos: v(m.ball.pos), vel: v(m.ball.vel), spin: v(m.ball.spin), onGround: m.ball.onGround },
    owner: m.owner?.id ?? -1, heldBy: m.heldBy?.id ?? -1, controlled: m.controlled.id, passTarget: m.passTarget?.id ?? -1,
    players: m.all.map((p) => ({
      id: p.id, pos: v(p.pos), vel: v(p.vel), facing: p.facing, action: p.action, actionT: p.actionT, stamina: p.stamina,
      moveX: p.moveX, moveZ: p.moveZ, wantSpeed: p.wantSpeed, plan: p.plan ? `${p.plan.type}:${p.plan.targetId}` : null,
      icept: (m.ai as any).intercept[p.id],
    })),
  };
}

/** Scripted thumbs, identical on both sides: the stick turns, buttons get tapped and held. */
function script(i: number, inp: InputState): void {
  const DIRS = [[1, 0], [0.7, 0.7], [0, 1], [-0.7, 0.7], [-1, 0], [-0.7, -0.7], [0, -1], [0.7, -0.7]];
  const k = Math.floor(i / 240) % 9;
  inp.moveX = k === 8 ? 0 : DIRS[k][0];
  inp.moveY = k === 8 ? 0 : DIRS[k][1];
  inp.sprint = Math.floor(i / 500) % 3 === 0;
  const slot = i % 300;
  const btn = (Math.floor(i / 300) % 3) as 0 | 1 | 2;
  if (slot === 0) {
    inp.events.push({ btn, kind: 'down', hold: 0 });
    inp.held[btn] = true;
  }
  if (inp.held[btn]) inp.holdTime[btn] = slot / 120;
  inp.swipe[btn] = inp.held[btn] && Math.floor(i / 300) % 4 === 1;
  if (slot === 40) {
    inp.events.push({ btn, kind: 'up', hold: 40 / 120, swipeUp: Math.floor(i / 300) % 4 === 1 });
    inp.held[btn] = false;
    inp.holdTime[btn] = 0;
  }
  if (i % 1000 === 500) inp.tackleSwipe = 'tackle';
}

// args: out seed mode(auto|human|club|drill:<kind>) steps [dumpStep dumpOut]
const [out, seedS, mode, stepsS, dumpS, dumpOut] = process.argv.slice(2);
const seed = Number(seedS);
const steps = Number(stepsS);
prepareGroundPasses();
let setup: MatchSetup | undefined;
if (mode === 'club') {
  const { Club } = await import('pwa/meta/club');
  setup = new Club().matchSetup(seed);
  writeFileSync(out + '.setup.json', JSON.stringify(setup));
}
const m = new Match(seed, setup);
m.autoPlay = mode === 'auto' || mode === 'club';
const drill = mode.startsWith('drill:') ? new Drill(m, mode.slice(6) as DrillKind) : null;
const inp = makeInput();
const hashes = new Uint32Array(steps);
for (let i = 0; i < steps; i++) {
  if (mode === 'human' || drill) script(i, inp);
  m.step(inp);
  drill?.step();
  inp.events.length = 0;
  inp.tackleSwipe = null;
  m.takeEvents();
  hashes[i] = hash(m);
  if (dumpS !== undefined && i === Number(dumpS)) writeFileSync(dumpOut, JSON.stringify(dump(m), null, 1));
}
writeFileSync(out, Buffer.from(hashes.buffer));
console.log(`${mode} seed ${seed}: ${steps} steps, ${m.teams[0].score}-${m.teams[1].score}, phase ${m.phase}${drill ? `, drill ${drill.line}` : ''}`);
