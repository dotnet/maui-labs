# Recipe: perspective coverflow

First verify whether the request means true perspective/depth/overlap or merely
snapping/scaling/masking. Read [geometry](geometry-and-anchoring.md),
[reuse](reuse-and-handler-lifecycle.md) and requested platform references.

## Contract

Define CoverflowView with predictable logical card extent, focal alignment,
perspective/tilt/scale/overlap, drawing order, snap/velocity policy, item invocation/
selection, keyboard traversal and reduced-motion behavior. Add looping only when
requested, with unique occurrence keys and bounded caches.

Material carousel keylines/masking and a flat scale carousel are different effects.
Do not silently ship a look-alike for exact perspective requirements.

## Native strategy

| Target | Candidate mechanism to compile/runtime prove |
|---|---|
| UIKit | UICollectionView custom attributes/Transform3D or appropriate visible-item hook |
| Android | RecyclerView custom/extended manager and bounded child rotation/camera/transforms |
| WinUI | Virtualizing backing layout plus native Composition/perspective transforms |
| AppKit | NSCollectionView geometry plus suitable item view/layer transforms; no layout-attributes Transform3D |
| GTK4 | Custom native virtualizer/transforms, overlap/input/accessibility bridge |
| WPF | Bounded Viewport3D/PerspectiveCamera and interactive Viewport2DVisual3D cards or verified equivalent |

WPF RenderTransform alone is 2D, not true perspective. A 3D host does not supply
virtualization automatically. Archived iCarousel provides historical native
pool/perspective evidence, not a current dependency recommendation.
[Sources U5, U9, A2, B9, P4](sources.md)

## Per-scroll pipeline

1. Maintain logical untransformed geometry/extent separately from transforms.
2. Locate a bounded candidate range, expanded for transformed visible bounds.
3. Reuse compatible card renderers and update only needed transforms.
4. Compute depth/drawing and hit-test order consistently.
5. Snap to declared focal position while preserving keyed selection/focus.
6. Reset every transform/accessibility/binding field when content is reused.

Do not animate layout constraints or rebuild every item rectangle each frame.
Native cached mutable attributes may require copying before transformation.
Perspective changes perceived bounds; clipping and tapping must match the result.

Infinite cycling needs presentation occurrence identity and coherent restoration,
not an enormous item count with repeated diffable IDs. Remove needless decoded
assets/listeners even when logical looping never ends.

## Proof

Assert real perspective/projection mechanism and bounded realized/native/MAUI/
texture counts. Test transformed hit targets, snap edges/velocity, resize, zero/
one items, keyboard/bring-into-view, accessibility order, reduced motion, data
updates and teardown. Profile actual compositor/native frame work plus managed
allocations. Compiled transform properties or screenshots are not proof of smooth,
interactive 3D behavior.
