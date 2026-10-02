export const requiredContracts = [
  'two-real-window-scenes', 'two-os-captured-url-contexts',
  ...['false', 'true'].flatMap(result => [`warm-result-${result}`, `warm-forwarding-${result}`, `warm-observers-${result}`]),
  'warm-observer-without-window', 'cold-pending-without-window', 'both-original-activations',
  'cold-drain-first', 'other-window-stays-pending', 'cold-drain-second', 'window-isolation',
  'cold-drained-exactly-once',
  ...['orders.open', 'qualification.unknown'].flatMap(type => [`shortcut-individual-${type}`, `shortcut-aggregate-${type}`]),
];

export const remainingGates = [
  'Released SDK 10.0.401 / workload 10.0.401.1 / macios release27.0.10722 / Xcode 27.0 build and device evidence.',
  'Independent native eval.yaml assessment of actual agent-produced migrations; this runner does not execute or score the agent evaluator.',
  'Native delivery of multiple URL contexts and multiple user activities in one scene connection.',
  'Real associated-domain universal-link / Handoff delivery (warm and cold).',
  'Actual authentication-provider return with valid app/provider configuration.',
  'OS-selected home-screen quick actions (warm and cold), including a real native completion.',
];

export function evaluate(events, { manifestId, startupProcess, coldProcess, warmUrls, coldUrl }) {
  const checks = [];
  const check = (name, pass) => checks.push({ name, pass: Boolean(pass) });
  const os = events.filter(e => e.delivery === 'os');
  const startup = os.filter(e => e.process === startupProcess);
  const cold = os.filter(e => e.process === coldProcess);
  check('embedded-candidate-identity', [startup, cold].every(group =>
    group.filter(e => e.kind === 'boot').length === 1 &&
    group.find(e => e.kind === 'boot')?.detail?.candidate?.id === manifestId));
  check('os-startup-and-both-activation-side-effects', startup.some(e =>
    e.kind === 'base.activate' && e.window &&
    ['base.connect', 'original.scene-activation', 'original.application-activation'].every(kind =>
      startup.some(other => other.kind === kind && other.scene === e.scene))));
  for (const [index, url] of warmUrls.entries()) {
    const effects = startup.filter(e => e.kind === 'original.warm-url' && e.value === url);
    const deliveries = startup.filter(e => e.kind === 'base.url' && e.detail?.urls?.includes(url));
    check(`os-warm-url-${index + 1}`, effects.length === 1 && deliveries.length === 1 &&
      effects[0].scene && effects[0].scene === deliveries[0].scene &&
      !startup.some(e => e.kind === 'original.cold-url' && e.value === url));
  }
  const coldEffects = cold.filter(e => e.kind === 'original.cold-url' && e.value === coldUrl);
  const coldDeliveries = cold.filter(e => e.kind === 'base.connect' && e.detail?.urls?.includes(coldUrl));
  check('os-cold-url-new-process', coldProcess && startupProcess !== coldProcess &&
    coldEffects.length === 1 && coldDeliveries.length === 1 &&
    coldEffects[0].scene && coldEffects[0].scene === coldDeliveries[0].scene &&
    coldEffects[0].window && coldEffects[0].detail?.title === coldUrl);
  const injected = events.filter(e => e.delivery === 'injected-contract' && e.process === startupProcess);
  for (const name of requiredContracts) {
    const matches = injected.filter(e => e.kind === 'assertion' && e.value === name);
    check(name, matches.length === 1 && matches[0].detail?.pass === true);
  }
  check('contract-completed-without-exception',
    injected.filter(e => e.kind === 'contract.complete').length === 1 &&
    !events.some(e => e.kind === 'contract.error') &&
    !injected.some(e => e.kind === 'assertion' && e.detail?.pass !== true));
  return {
    schema: 1,
    runtimeSubset: checks.every(c => c.pass) ? 'passed' : 'failed',
    qualification: 'blocked',
    nativeEvaluator: 'not-assessed',
    coverage: {
      osDispatch: 'Only recorded startup, separate warm URLs, and the new-process single cold URL.',
      injectedCallbacks: 'Explicit invocation on real MAUI scenes; not OS callback dispatch.',
      injectedColdCollections: 'CaptureCold extraction-method contract only; does not validate UIKit collection extraction, native multiple connection contexts, or real cold activity delivery.',
      authenticationAndHandoff: 'Not exercised.',
      priorGeneratedArtifacts: 'Not exercised or qualified by this run.',
    },
    checks,
    remainingGates,
  };
}
