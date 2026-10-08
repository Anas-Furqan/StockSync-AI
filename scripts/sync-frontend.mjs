import {
  access,
  cp,
  mkdir,
  rename,
  rm,
  stat,
} from 'node:fs/promises'
import { watch } from 'node:fs'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url))
const projectRoot = path.resolve(scriptDirectory, '..')
export const defaultSource = path.join(projectRoot, 'frontend', 'dist')
export const defaultDestination = path.join(projectRoot, 'desktop', 'renderer')

async function validateSource(source) {
  let indexStats
  try {
    indexStats = await stat(path.join(source, 'index.html'))
  } catch {
    throw new Error(
      `Frontend build output is missing at ${source}. Run "npm run build --workspace frontend" first.`,
    )
  }

  if (!indexStats.isFile()) {
    throw new Error(`Frontend build output has no index.html file at ${source}.`)
  }
}

export async function syncFrontend({
  source = defaultSource,
  destination = defaultDestination,
} = {}) {
  await validateSource(source)
  await mkdir(path.dirname(destination), { recursive: true })

  const staging = `${destination}.staging`
  const previous = `${destination}.previous`
  await rm(staging, { recursive: true, force: true })
  await cp(source, staging, { recursive: true, errorOnExist: false })
  await access(path.join(staging, 'index.html'))

  await rm(previous, { recursive: true, force: true })
  let movedExisting = false
  try {
    await rename(destination, previous)
    movedExisting = true
  } catch (error) {
    if (error.code !== 'ENOENT') {
      await rm(staging, { recursive: true, force: true })
      throw error
    }
  }

  try {
    await rename(staging, destination)
    await rm(previous, { recursive: true, force: true })
  } catch (error) {
    if (movedExisting) {
      await rename(previous, destination).catch(() => {})
    }
    await rm(staging, { recursive: true, force: true })
    throw error
  }

  return destination
}

async function runWatchMode() {
  await syncFrontend()
  process.stdout.write(`Synced ${defaultSource} -> ${defaultDestination}\n`)

  let timer = null
  let syncing = Promise.resolve()
  const watcher = watch(defaultSource, { recursive: true }, () => {
    clearTimeout(timer)
    timer = setTimeout(() => {
      syncing = syncing
        .then(() => syncFrontend())
        .then(() => process.stdout.write(`Synced at ${new Date().toLocaleTimeString()}\n`))
        .catch((error) => process.stderr.write(`Sync failed: ${error.message}\n`))
    }, 400)
  })

  const stop = () => {
    clearTimeout(timer)
    watcher.close()
    process.stdout.write('Sync watcher stopped.\n')
    process.exit(0)
  }
  process.once('SIGINT', stop)
  process.once('SIGTERM', stop)
}

async function main() {
  if (process.argv.includes('--watch')) {
    await runWatchMode()
  } else {
    const destination = await syncFrontend()
    process.stdout.write(`Synced frontend assets to ${destination}\n`)
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => {
    process.stderr.write(`${error.message}\n`)
    process.exitCode = 1
  })
}
