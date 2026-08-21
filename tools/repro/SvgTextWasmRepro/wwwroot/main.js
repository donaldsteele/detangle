// Starts the runtime and runs Main. Everything the reproduction reports goes to the
// browser console, which is the only output device this platform has.
import { dotnet } from './_framework/dotnet.js';

const { runMain } = await dotnet.create();

await runMain();
