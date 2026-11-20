import { dotnet } from './_framework/dotnet.js';

const { runMain } = await dotnet.create();
await runMain();
