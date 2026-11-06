# FabricUI

FeedFabric's Vue 3 components and design tokens. The admin panel (`web/admin`) is built with them, and they're
there for any front end you build on FeedFabric.

```bash
make fabric          # the gallery: every component in every state, at http://localhost:5190
cd web/fabric-ui && npm test
```

## Using it

Apps use FabricUI from source; there's no package to publish. In the app's `vite.config.ts`, alias
`@feedfabric/fabric-ui` to `../fabric-ui/src/index.ts` and dedupe `vue` (see `web/admin/vite.config.ts`), then:

```ts
import '@feedfabric/fabric-ui/styles.css'   // once, before the app's own styles
import { FabricButton, FabricCard, useToast } from '@feedfabric/fabric-ui'
```

```vue
<FabricCard title="Review queue">
  <FabricButton variant="primary" icon="check" @click="approve">Approve</FabricButton>
</FabricCard>
<FabricToaster />   <!-- once, near the root -->
```

## What's in it

| | |
|---|---|
| Layout | `FabricAppShell` `FabricSidebar` `FabricPage` `FabricCard` `FabricStack` `FabricGrid` |
| Content | `FabricList` `FabricListItem` `FabricTable` `FabricStatCard` `FabricBadge` `FabricChip` `FabricAvatar` `FabricIcon` `FabricEmptyState` `FabricSkeleton` `FabricDisclosure` `FabricTabs` `FabricPager` |
| Forms | `FabricButton` `FabricField` `FabricInput` `FabricTextarea` `FabricSelect` `FabricSwitch` `FabricSegmented` `FabricRangeSlider` `FabricFileUpload` |
| Feedback | `FabricNotice` `FabricToaster` (`useToast`) `FabricProgress` `FabricMeter` `FabricSpinner` `FabricTooltip` |
| Overlays | `FabricDialog` `FabricConfirmDialog` `FabricMenu` |
| Theme | `FabricThemeToggle` (`useTheme`) and the tokens in `src/styles/fabric.css` |

## Conventions

- **Tokens, not colours.** Everything is drawn with `--fabric-*` custom properties: a strong and a soft colour for
  each tone (accent, good, warn, bad, info), a spacing scale (`--fabric-space-0` to `-8`), radii, text sizes and
  motion. Dark mode follows the system unless `<html data-theme>` says otherwise.
- **Accessible by default.** Fields wire their label, hint and error to the control; segmented controls, tabs and
  menus follow the ARIA patterns, keyboard included; dialogs use the native `<dialog>`; toasts announce themselves,
  problems at once. Icon-only buttons need a `label`.
- **v-model everywhere** a component holds a value (`v-model`, `v-model:open`, `v-model:selected`).
- **Works at phone width.** Grids reflow, tables scroll inside their card, and the app shell folds into a menu.
