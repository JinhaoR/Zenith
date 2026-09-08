// Reproducibly collect the notices for the code included in engine.js.
import fs from 'node:fs';
import path from 'node:path';
const lock = JSON.parse(fs.readFileSync('package-lock.json', 'utf8'));
const notices = [
  'Zenith filtering third-party notices',
  'EasyList and EasyPrivacy: The EasyList authors (https://easylist.to/).',
  'Unmodified snapshots downloaded 2026-09-07 from:',
  'https://easylist.to/easylist/easylist.txt',
  'https://easylist.to/easylist/easyprivacy.txt',
  'Used under Creative Commons Attribution-ShareAlike 3.0 Unported:',
  'https://creativecommons.org/licenses/by-sa/3.0/ (or any later version).',
  'License and attribution: https://easylist.to/pages/licence.html',
  'The list source files and their upstream version/commit headers are in the Zenith repository.',
  '',
  'Ghostery and dependency source packages are identified below; exact source archives and',
  'integrity hashes are recorded in tools/adblock/package-lock.json. Rebuild instructions',
  'and the unminified integration source are in tools/adblock/README.md.',
];
for (const [directory, metadata] of Object.entries(lock.packages)) {
  if (!directory || metadata.dev || !fs.existsSync(directory)) continue;
  const pkg = JSON.parse(fs.readFileSync(path.join(directory, 'package.json'), 'utf8'));
  const license = fs.readdirSync(directory).find(name => /^licen[cs]e(?:\.(?:txt|md))?$/i.test(name));
  if (!license) throw new Error(`No license found for ${pkg.name}`);
  notices.push('', '='.repeat(72), `${pkg.name} ${pkg.version}`, `Source: ${metadata.resolved}`, fs.readFileSync(path.join(directory, license), 'utf8'));
}
fs.writeFileSync('../../src/Zenith.App/Filtering/Assets/ENGINE-NOTICES.txt', notices.join('\n'));

// Optional NuGet cache path, after dotnet restore, for the native runtime notices.
if (process.argv[2]) {
  const runtime = path.join(process.argv[2], 'microsoft.clearscript.v8.native.win-x64', '7.5.1.1', 'licenses');
  const parts = ['Microsoft ClearScript / native V8 7.5.1.1 notices', 'https://github.com/ClearFoundry/ClearScript'];
  const collect = directory => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const file = path.join(directory, entry.name);
      if (entry.isDirectory()) collect(file);
      else parts.push('', path.relative(runtime, file), fs.readFileSync(file, 'utf8'));
    }
  };
  collect(runtime);
  fs.writeFileSync('../../src/Zenith.App/Filtering/Assets/RUNTIME-NOTICES.txt', parts.join('\n'));
}
