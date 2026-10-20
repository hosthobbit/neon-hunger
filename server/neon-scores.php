<?php
// NEON HUNGER public scoreboard (best ever, one entry per name: only your best counts).
//   GET  ?top=20  -> {"scores":[{name, score, level, won, date}, ...]}
//   POST name, levels, score, v
//        -> {"ok":true,"rank":N,"best":bool,"score":S} or {"ok":false,"error":"..."}
// "levels" is the run as per-level counts: "L:nodes,powers,gems,risk,c1,c2,c3,c4,cleared,seconds;..."
// The score is recomputed here with the game's point values (Scoring.cs) and every count is checked
// against what that level actually contains, so a typed-in score or an impossible run is rejected.

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store, max-age=0');
$origin = $_SERVER['HTTP_ORIGIN'] ?? '';
if ($origin === 'https://hosthobbit.github.io'
    || preg_match('#^http://(localhost|127\.0\.0\.1|192\.168\.\d+\.\d+|10\.\d+\.\d+\.\d+)(:\d+)?$#', $origin)) {
    header('Access-Control-Allow-Origin: ' . $origin);
    header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
    header('Access-Control-Allow-Headers: Content-Type');
    header('Vary: Origin');
}
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') { http_response_code(204); exit; }

function out($data, $code = 200) { http_response_code($code); echo json_encode($data); exit; }
function fail($error, $code = 400) { out(['ok' => false, 'error' => $error], $code); }

// what each level contains (LevelDef in Levels.cs)
const LEVELS = [
    1 => ['nodes' => 175, 'powers' => 4, 'gems' => 1, 'risk' => 1, 'par' => 140],
    2 => ['nodes' => 203, 'powers' => 4, 'gems' => 2, 'risk' => 2, 'par' => 170],
    3 => ['nodes' => 250, 'powers' => 4, 'gems' => 4, 'risk' => 2, 'par' => 200],
];
const P_NODE = 10, P_POWER = 50, P_GEM = 300, P_RISK = 750;

$home = getenv('HOME') ?: dirname($_SERVER['DOCUMENT_ROOT'] ?? __DIR__);
$dataDir = getenv('NEON_SCORES_DIR') ?: $home . '/game-scores';
if (!is_dir($dataDir) && !@mkdir($dataDir, 0700, true)) fail('storage unavailable', 500);
try {
    $db = new PDO('sqlite:' . $dataDir . '/neon-scores.sqlite');
    $db->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
    $db->exec('PRAGMA busy_timeout = 3000');
    $db->exec('CREATE TABLE IF NOT EXISTS scores (
        name TEXT PRIMARY KEY, score INTEGER NOT NULL, level INTEGER NOT NULL, won INTEGER NOT NULL,
        created INTEGER NOT NULL, ip TEXT NOT NULL)');
    $db->exec('CREATE INDEX IF NOT EXISTS score_rank ON scores (score DESC)');
    $db->exec('CREATE TABLE IF NOT EXISTS submits (ip TEXT NOT NULL, created INTEGER NOT NULL)');
} catch (Exception $e) {
    fail('storage unavailable', 500);
}

if ($_SERVER['REQUEST_METHOD'] === 'GET') {
    $top = max(1, min(50, (int)($_GET['top'] ?? 20)));
    $rows = $db->query("SELECT * FROM scores ORDER BY score DESC, created ASC LIMIT $top")->fetchAll(PDO::FETCH_ASSOC);
    out(['scores' => array_map(function ($r) {
        return ['name' => $r['name'], 'score' => (int)$r['score'], 'level' => (int)$r['level'], 'won' => (bool)$r['won'], 'date' => gmdate('Y-m-d', (int)$r['created'])];
    }, $rows)]);
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') fail('method not allowed', 405);
$in = $_POST;
if (!$in) { $j = json_decode(file_get_contents('php://input', false, null, 0, 4000) ?: '', true); if (is_array($j)) $in = $j; }
if ((int)($in['v'] ?? 0) !== 1) fail('Please refresh the game to submit scores.');
$claimed = filter_var($in['score'] ?? null, FILTER_VALIDATE_INT);
if ($claimed === false || $claimed < 0 || $claimed > 10000000) fail('bad score');

$levels = (string)($in['levels'] ?? '');
if (strlen($levels) > 400 || !preg_match('/^\d:(\d+,){9}\d+(\.\d+)?(;\d:(\d+,){9}\d+(\.\d+)?)*$/', $levels)) fail('bad levels');
$score = 0; $reached = 0; $won = false;
$parts = explode(';', $levels);
foreach ($parts as $i => $part) {
    [$lv, $rest] = explode(':', $part, 2);
    $lv = (int)$lv;
    if ($lv !== $i + 1 || !isset(LEVELS[$lv])) fail("That run doesn't add up.");
    $L = LEVELS[$lv];
    $f = explode(',', $rest);
    [$n, $p, $g, $r, $c1, $c2, $c3, $c4, $cleared] = array_map('intval', array_slice($f, 0, 9));
    $secs = (float)$f[9];
    $last = $i === count($parts) - 1;
    if ($n > $L['nodes'] || $p > $L['powers'] || $g > $L['gems'] || $r > $L['risk']) fail("That run doesn't add up.");
    // each surge catches each hunter at most once, in order: 1st catches <= surges, 2nd <= 1st, ...
    if ($c1 > $p || $c2 > $c1 || $c3 > $c2 || $c4 > $c3) fail("That run doesn't add up.");
    if (!$last && $cleared !== 1) fail("That run doesn't add up.");
    if ($cleared === 1) {
        if ($n !== $L['nodes']) fail("That run doesn't add up.");
        if ($secs < $L['nodes'] / 7.5 || $secs > 7200) fail("That run doesn't add up.");
    }
    $score += $n * P_NODE + $p * P_POWER + $g * P_GEM + $r * P_RISK + $c1 * 200 + $c2 * 400 + $c3 * 800 + $c4 * 1600;
    if ($cleared === 1) $score += 1000 * $lv + max(0, (int)floor($L['par'] - $secs)) * 10;
    $reached = $lv;
    if ($cleared === 1 && $lv === 3) $won = true;
}
if ($score !== $claimed) fail('score mismatch');

$name = trim(preg_replace('/\s+/u', ' ', (string)($in['name'] ?? '')));
if (!preg_match("/^[\\p{L}\\p{N} '._-]{2,16}$/u", $name)) fail('Names can be 2 to 16 letters or numbers.');
$squashed = strtolower(preg_replace('/[^a-z]/i', '', strtr($name, '013457@$', 'oieastas'))) . ' '
          . strtolower(preg_replace('/[^a-z]/i', '', strtr($name, '013457@$', 'oleastas')));
foreach (['fuck', 'shit', 'cunt', 'bitch', 'bastard', 'nigg', 'fag', 'twat', 'wank', 'piss', 'cock', 'dick', 'pussy', 'slut', 'whore', 'rape', 'nazi', 'hitler', 'retard', 'arse', 'asshole', 'bollock', 'spastic', 'paki', 'kkk'] as $bad) {
    if (strpos($squashed, $bad) !== false) fail('Please pick a different name.');
}

$ip = hash('sha256', ($_SERVER['REMOTE_ADDR'] ?? '') . '|neon-scores');
$db->prepare('DELETE FROM submits WHERE created < ?')->execute([time() - 3600]);
$recent = $db->prepare('SELECT COUNT(*) FROM submits WHERE ip = ?');
$recent->execute([$ip]);
if ((int)$recent->fetchColumn() >= 30) fail('Too many scores this hour, try again later.', 429);
$db->prepare('INSERT INTO submits (ip, created) VALUES (?, ?)')->execute([$ip, time()]);

$prev = $db->prepare('SELECT score FROM scores WHERE name = ?');
$prev->execute([$name]);
$old = $prev->fetchColumn();
$best = $old === false || $score > (int)$old;
if ($best) {
    $db->prepare('INSERT OR REPLACE INTO scores (name, score, level, won, created, ip) VALUES (?, ?, ?, ?, ?, ?)')
       ->execute([$name, $score, $reached, $won ? 1 : 0, time(), $ip]);
}
$mine = $best ? $score : (int)$old;
$rank = $db->prepare('SELECT COUNT(*) FROM scores WHERE score > ?');
$rank->execute([$mine]);
out(['ok' => true, 'rank' => (int)$rank->fetchColumn() + 1, 'best' => $best, 'score' => $mine]);
