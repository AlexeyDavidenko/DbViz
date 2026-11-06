// DbViz: installs notes and graph settings into an EXISTING Obsidian vault (macOS, JavaScript for Automation).
// Usage: the "(macOS).command" launcher, or  osascript -l JavaScript install.js <installer folder> [<vault>] [--notes|--no-notes] [--yes]
// Changes only: .obsidian/graph.json (own color groups + arrows), bookmarks.json (own bookmark group),
// core-plugins.json (enables graph/bookmarks/...). Backups *.dbviz-backup-* are made before any change.
ObjC.import('Foundation');

const fm = $.NSFileManager.defaultManager;
const app = Application.currentApplication();
app.includeStandardAdditions = true;

function exists(p) { return fm.fileExistsAtPath($(p)); }
function read(p) {
  const s = $.NSString.stringWithContentsOfFileEncodingError($(p), $.NSUTF8StringEncoding, null);
  return s.isNil() ? null : ObjC.unwrap(s);
}
function write(p, text) {
  if (!$(text).writeToFileAtomicallyEncodingError($(p), true, $.NSUTF8StringEncoding, null)) throw new Error('Could not write ' + p);
}
function readJson(p, fallback) {
  const t = read(p);
  if (t === null) return fallback;
  return JSON.parse(t.replace(/^﻿/, ''));
}
function stamp() {
  const d = new Date(), z = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}${z(d.getMonth() + 1)}${z(d.getDate())}-${z(d.getHours())}${z(d.getMinutes())}${z(d.getSeconds())}`;
}
function backup(p) {
  if (exists(p)) fm.copyItemAtPathToPathError($(p), $(p + '.dbviz-backup-' + stamp()), null);
}
function saveJson(p, obj) { backup(p); write(p, JSON.stringify(obj, null, 2)); }

function mergeGraph(graph, payload) {
  const groups = (graph.colorGroups || []).filter(g => !payload.groupMarkers.some(m => String(g.query || '').includes(m)));
  graph.colorGroups = groups.concat(payload.colorGroups);
  graph.showArrow = true;
  return graph;
}
function mergeBookmarks(bm, payload) {
  const title = payload.bookmarkGroup.title;
  bm.items = (bm.items || []).filter(i => !(i.type === 'group' && i.title === title));
  bm.items.push(payload.bookmarkGroup);
  return bm;
}
function mergeCore(core, payload) {
  if (Array.isArray(core)) { payload.corePlugins.forEach(p => { if (!core.includes(p)) core.push(p); }); return core; }
  payload.corePlugins.forEach(p => { core[p] = true; });
  return core;
}

function run(argv) {
  const installDir = argv[0];
  let vault = argv[1] && !argv[1].startsWith('--') ? argv[1] : null;
  const yes = argv.includes('--yes');
  const payload = readJson(installDir + '/dbviz-obsidian-settings.json', null);
  if (!payload) throw new Error('dbviz-obsidian-settings.json not found next to the script');

  if (!yes) app.displayDialog('QUIT Obsidian (Cmd+Q) before installing, otherwise it will overwrite the settings.\n\nNext, choose your vault folder.',
    { buttons: ['Cancel', 'Continue'], defaultButton: 'Continue', cancelButton: 'Cancel', withTitle: 'DbViz' });
  if (!vault) vault = app.chooseFolder({ withPrompt: 'Your Obsidian vault folder (the one containing .obsidian)' }).toString();
  vault = vault.replace(/\/$/, '');
  const cfg = vault + '/.obsidian';
  if (!exists(cfg)) throw new Error('No .obsidian folder here, this is not an Obsidian vault: ' + vault);

  // notes
  const src = installDir + '/../' + payload.notesFolder;
  const dst = vault + '/' + payload.notesFolder;
  let copyNotes = argv.includes('--notes') ? true : argv.includes('--no-notes') ? false : null;
  if (copyNotes === null) {
    const msg = exists(dst)
      ? `The folder "${payload.notesFolder}" already exists in the vault. Notes will NOT be overwritten; update them manually if needed.`
      : `Copy the schema notes (folder "${payload.notesFolder}") into the vault?`;
    copyNotes = exists(dst) ? false
      : app.displayDialog(msg, { buttons: ['Settings only', 'Notes and settings'], defaultButton: 'Notes and settings', withTitle: 'DbViz' })
          .buttonReturned === 'Notes and settings';
  }
  let notesInfo = 'notes were not copied';
  if (copyNotes && !exists(dst)) {
    if (!fm.copyItemAtPathToPathError($(src), $(dst), null)) throw new Error('Could not copy the notes to ' + dst);
    notesInfo = 'notes copied to ' + payload.notesFolder;
  }

  saveJson(cfg + '/graph.json', mergeGraph(readJson(cfg + '/graph.json', {}), payload));
  saveJson(cfg + '/bookmarks.json', mergeBookmarks(readJson(cfg + '/bookmarks.json', {}), payload));
  saveJson(cfg + '/core-plugins.json', mergeCore(readJson(cfg + '/core-plugins.json', {}), payload));

  const done = `Done: ${notesInfo}; added layer colors (${payload.colorGroups.length}) and the "${payload.bookmarkGroup.title}" bookmarks.\n` +
    'Open Obsidian: Bookmarks → DBVisualization group. Backups: .obsidian/*.dbviz-backup-*';
  if (!yes) app.displayDialog(done, { buttons: ['OK'], withTitle: 'DbViz' });
  return done;
}
