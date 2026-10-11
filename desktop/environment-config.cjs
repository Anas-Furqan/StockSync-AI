const path = require('node:path')
const { mkdir, writeFile } = require('node:fs/promises')

const defaultEnvironmentFile = `# Local StockSync AI configuration. Process environment variables override these values.
GEMINI_API_KEY=
GEMINI_MODEL=gemini-2.5-flash
GEMINI_REQUEST_TIMEOUT_SECONDS=90
GEMINI_MAX_RETRIES=2

# POS SQL Server — Read-only connection
STOCKSYNC_POS_CONNECTION_STRING=

# POS SQL Server — Separate write connection
STOCKSYNC_POS_WRITE_CONNECTION_STRING=

# Keep both safety flags disabled until live schema and business conventions are verified.
STOCKSYNC_POS_BUSINESS_CONVENTIONS_VERIFIED=false
STOCKSYNC_POS_WRITES_ENABLED=false
`

function resolveEnvironmentFile({ isPackaged, userDataPath, appPath }) {
  return isPackaged
    ? path.join(userDataPath, '.env')
    : path.join(appPath, '.env')
}

async function ensureEnvironmentFile(environmentFile) {
  await mkdir(path.dirname(environmentFile), { recursive: true })
  try {
    await writeFile(environmentFile, defaultEnvironmentFile, {
      encoding: 'utf8',
      flag: 'wx',
      mode: 0o600,
    })
  } catch (error) {
    if (error?.code !== 'EEXIST') {
      throw error
    }
  }
}

module.exports = { defaultEnvironmentFile, ensureEnvironmentFile, resolveEnvironmentFile }
