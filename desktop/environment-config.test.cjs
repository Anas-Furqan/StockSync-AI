const { test } = require('node:test')
const assert = require('node:assert/strict')
const { mkdtemp, readFile, rm, writeFile } = require('node:fs/promises')
const os = require('node:os')
const path = require('node:path')
const {
  ensureEnvironmentFile,
  resolveEnvironmentFile,
} = require('./environment-config.cjs')

test('development and packaged apps resolve distinct writable env paths', () => {
  assert.equal(resolveEnvironmentFile({
    isPackaged: false,
    userDataPath: 'C:\\Users\\test\\AppData\\Roaming\\StockSync AI',
    appPath: 'C:\\source\\StockSync AI',
  }), path.join('C:\\source\\StockSync AI', '.env'))
  assert.equal(resolveEnvironmentFile({
    isPackaged: true,
    userDataPath: 'C:\\Users\\test\\AppData\\Roaming\\StockSync AI',
    appPath: 'C:\\Program Files\\StockSync AI',
  }), path.join('C:\\Users\\test\\AppData\\Roaming\\StockSync AI', '.env'))
})

test('new local env files contain POS settings with disabled write guards', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'stocksync-env-'))
  const environmentFile = path.join(directory, 'profile', '.env')
  try {
    await ensureEnvironmentFile(environmentFile)
    const contents = await readFile(environmentFile, 'utf8')
    assert.match(contents, /STOCKSYNC_POS_CONNECTION_STRING=/)
    assert.match(contents, /STOCKSYNC_POS_WRITE_CONNECTION_STRING=/)
    assert.match(contents, /STOCKSYNC_POS_BUSINESS_CONVENTIONS_VERIFIED=false/)
    assert.match(contents, /STOCKSYNC_POS_WRITES_ENABLED=false/)
    assert.match(contents, /GEMINI_API_KEY=/)

    await writeFile(environmentFile, 'GEMINI_API_KEY=preserved-secret\n', 'utf8')
    await ensureEnvironmentFile(environmentFile)
    assert.equal(await readFile(environmentFile, 'utf8'), 'GEMINI_API_KEY=preserved-secret\n')
  } finally {
    await rm(directory, { recursive: true, force: true })
  }
})
