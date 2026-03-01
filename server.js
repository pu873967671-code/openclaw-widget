#!/usr/bin/env node
/**
 * OpenClaw Widget 数据服务 v6
 * 动态配置：models/network 从 widget-config.json 读取，tasks 从 openclaw cron list 拉取
 */
const http = require('http');
const { execSync, exec: cpExec } = require('child_process');
const fs = require('fs');
const path = require('path');

const PORT = 4200;
const CLASH_SECRET = '0424';
const OC_BIN = '/home/pupu/.nvm/versions/node/v24.13.0/bin/openclaw';
const NODE_PATH = '/home/pupu/.nvm/versions/node/v24.13.0/bin';
const CONFIG_PATH = path.join(__dirname, 'widget-config.json');

let clashHost;
try { clashHost = execSync("ip route | awk '/default/ {print $3}'").toString().trim(); }
catch { clashHost = '172.19.208.1'; }

// --- Config loader (hot-reload on each /status) ---
let configMtime = 0;
let config = { models: [], network: [] };

function loadConfig() {
  try {
    const stat = fs.statSync(CONFIG_PATH);
    if (stat.mtimeMs !== configMtime) {
      config = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8'));
      configMtime = stat.mtimeMs;
      console.log('Config reloaded:', config.models.length, 'models,', config.network.length, 'network targets');
    }
  } catch (e) {
    console.error('Config load error:', e.message);
  }
}

// --- Cron tasks loader ---
let cronCache = null;
let cronCacheTime = 0;
const CRON_CACHE_TTL = 60000;

const CRON_JOBS_PATH = '/home/pupu/.openclaw/cron/jobs.json';

function getCronTasks() {
  const now = Date.now();
  if (cronCache && now - cronCacheTime < CRON_CACHE_TTL) return cronCache;
  try {
    const raw = fs.readFileSync(CRON_JOBS_PATH, 'utf8');
    const data = JSON.parse(raw);
    const jobs = data.jobs || [];
    cronCache = jobs.map(j => ({
      name: j.name || j.id,
      enabled: j.enabled !== false,
      id: j.id,
    }));
    cronCacheTime = now;
  } catch (e) {
    console.error('Cron jobs read error:', e.message);
    if (!cronCache) cronCache = [];
  }
  return cronCache;
}

// --- Check helpers ---
function curlCheck(url, timeout, extra) {
  const t0 = Date.now();
  try {
    const cmd = `curl -s -o /dev/null -w "%{http_code}" --max-time ${timeout} ${extra || ''} "${url}" 2>/dev/null`;
    const out = execSync(cmd, { timeout: (timeout + 2) * 1000 }).toString().trim();
    return { code: parseInt(out) || 0, ms: Date.now() - t0 };
  } catch {
    return { code: 0, ms: Date.now() - t0 };
  }
}

function curlBody(url, timeout, extra) {
  try {
    return execSync(`curl -s --max-time ${timeout} ${extra || ''} "${url}" 2>/dev/null`, { timeout: (timeout + 2) * 1000 }).toString();
  } catch { return ''; }
}

function checkModel(m) {
  const r = curlCheck(m.url, 8, '--insecure');
  // sslIssue providers: code 0 也算 OK（curl SSL 握手失败但实际可用）
  // 其他 provider: 只有特定 HTTP code 才算正常
  const okCodes = m.okCodes || [200, 401, 403, 404];
  const ok = m.sslIssue ? (r.code === 0 || okCodes.includes(r.code)) : okCodes.includes(r.code);
  return { name: m.name, ok, latency: r.ms + 'ms', code: r.code };
}

function checkNetwork(n) {
  if (n.type === 'http') {
    const r = curlCheck(n.url, 5, '--insecure');
    return { name: n.name, ok: r.code > 0, latency: r.ms + 'ms' };
  } else if (n.type === 'clash') {
    const r = curlCheck(
      'http://' + clashHost + ':9090/version', 3,
      '--noproxy "*" -H "Authorization: Bearer ' + CLASH_SECRET + '"'
    );
    if (r.code === 200) {
      const body = curlBody(
        'http://' + clashHost + ':9090/version', 3,
        '--noproxy "*" -H "Authorization: Bearer ' + CLASH_SECRET + '"'
      );
      try {
        const ver = JSON.parse(body).version || '?';
        return { name: 'Clash ' + ver, ok: true, latency: r.ms + 'ms' };
      } catch {
        return { name: 'Clash', ok: true, latency: r.ms + 'ms' };
      }
    }
    return { name: 'Clash', ok: false, latency: r.ms + 'ms' };
  } else if (n.type === 'ping') {
    const host = n.host || '127.0.0.1';
    try {
      const out = execSync('ping -c 1 -W 2 ' + host + ' 2>&1', { timeout: 5000 }).toString();
      return { name: n.name, ok: !out.includes('100% packet loss') };
    } catch {
      return { name: n.name, ok: false };
    }
  }
  return { name: n.name, ok: false, error: 'unknown type' };
}

// --- Main status ---
function getStatus() {
  loadConfig();
  const result = {
    models: [], network: [], tasks: [], usage: {},
    ts: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
  };

  for (const m of config.models) {
    result.models.push(checkModel(m));
  }

  for (const n of config.network) {
    result.network.push(checkNetwork(n));
  }

  result.tasks = getCronTasks();
  // Gateway health check
  try {
    const hr = curlCheck('http://127.0.0.1:18792/', 3, '--noproxy "*"');
    result.health = { ok: hr.code === 200, code: hr.code, latency: hr.ms + 'ms' };
  } catch {
    result.health = { ok: false, code: 0 };
  }
  result.usage = { model: 'claude-proxy/claude-opus-4-6', status: 'running' };
  return result;
}

// --- Cache ---
let cache = null, cacheTime = 0;
const CACHE_TTL = 30000;

const { URL } = require('url');

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Content-Type', 'application/json; charset=utf-8');

  const u = new URL(req.url, 'http://localhost');

  if (u.pathname === '/status') {
    const now = Date.now();
    if (!cache || now - cacheTime > CACHE_TTL) {
      cache = getStatus();
      cacheTime = now;
    }
    res.end(JSON.stringify(cache));

  } else if (u.pathname === '/check') {
    loadConfig();
    const type = u.searchParams.get('type');
    const name = u.searchParams.get('name');
    if (type === 'model') {
      const m = config.models.find(x => x.name === name);
      if (m) {
        res.end(JSON.stringify(checkModel(m)));
      } else {
        res.end(JSON.stringify({ name, ok: false, error: 'unknown model' }));
      }
    } else if (type === 'network') {
      const n = config.network.find(x => x.name === name || name.startsWith(x.name));
      if (n) {
        res.end(JSON.stringify(checkNetwork(n)));
      } else {
        res.end(JSON.stringify({ name, ok: false, error: 'unknown network' }));
      }
    } else {
      res.end('{"error":"bad type"}');
    }

  } else if (u.pathname === '/run-task') {
    const name = u.searchParams.get('name');
    // 动态查找 cron job by name
    const tasks = getCronTasks();
    const task = tasks.find(t => t.name === name);
    if (!task) {
      res.end(JSON.stringify({ ok: false, name, error: 'unknown task' }));
    } else {
      cpExec(OC_BIN + ' cron run ' + task.id, {
        timeout: 15000,
        env: { ...process.env, PATH: NODE_PATH + ':' + (process.env.PATH || '') },
      }, (err, stdout, stderr) => {
        if (err && err.code) {
          res.end(JSON.stringify({ ok: false, name, id: task.id, error: (stdout || stderr || err.message).trim().slice(0, 200) }));
        } else {
          res.end(JSON.stringify({ ok: true, name, id: task.id, message: 'triggered' }));
        }
      });
    }

  } else if (u.pathname === '/health') {
    // Gateway health only
    const hr = curlCheck('http://127.0.0.1:18792/', 3, '--noproxy "*"');
    res.end(JSON.stringify({ ok: hr.code === 200, code: hr.code, latency: hr.ms + 'ms' }));

  } else if (u.pathname === '/switch-model') {
    // 切换到指定模型
    const name = u.searchParams.get('name');
    if (!name) {
      res.end(JSON.stringify({ ok: false, error: 'missing name' }));
      return;
    }
    try {
      const switchCmd = OC_BIN + ' models set ' + name;
      const out = execSync(switchCmd, {
        timeout: 10000,
        env: { ...process.env, PATH: NODE_PATH + ':' + (process.env.PATH || '') },
      }).toString();
      res.end(JSON.stringify({ ok: true, model: name, output: out.slice(0, 200) }));
    } catch (e) {
      res.end(JSON.stringify({ ok: false, model: name, error: e.message }));
    }

  } else if (u.pathname === '/switch-fastest') {
    // 找延迟最低的模型并切换
    loadConfig();
    const results = config.models.map(m => checkModel(m)).filter(r => r.ok);
    if (results.length === 0) {
      res.end(JSON.stringify({ ok: false, error: 'no healthy models' }));
      return;
    }
    // 按延迟排序，取最低
    results.sort((a, b) => parseInt(a.latency) - parseInt(b.latency));
    const fastest = results[0];
    // 调用 gateway 切换模型
    try {
      const switchCmd = OC_BIN + ' models set ' + fastest.name;
      const out = execSync(switchCmd, {
        timeout: 10000,
        env: { ...process.env, PATH: NODE_PATH + ':' + (process.env.PATH || '') },
      }).toString();
      res.end(JSON.stringify({ ok: true, model: fastest.name, latency: fastest.latency, output: out.slice(0, 200) }));
    } catch (e) {
      res.end(JSON.stringify({ ok: false, model: fastest.name, error: e.message }));
    }

  } else if (u.pathname === '/config') {
    // 返回当前配置（方便调试）
    loadConfig();
    res.end(JSON.stringify({ config, cronTasks: getCronTasks() }));

  } else {
    res.statusCode = 404;
    res.end('{"error":"not found"}');
  }
});

server.listen(PORT, '0.0.0.0', () => {
  console.log('Widget Server v6 on http://0.0.0.0:' + PORT);
  loadConfig();
  cache = getStatus();
  cacheTime = Date.now();
  console.log('Models:', cache.models.map(m => m.name + ':' + m.code).join(', '));
  console.log('Network:', cache.network.map(n => n.name + ':' + (n.ok ? 'OK' : 'FAIL')).join(', '));
  console.log('Tasks:', cache.tasks.map(t => t.name + ':' + (t.enabled ? 'ON' : 'OFF')).join(', '));
});
