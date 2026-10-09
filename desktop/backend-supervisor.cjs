const { EventEmitter } = require('node:events')
const { createServer } = require('node:net')
const { spawn } = require('node:child_process')
const { randomBytes } = require('node:crypto')
const path = require('node:path')

const delay = (milliseconds) =>
  new Promise((resolve) => setTimeout(resolve, milliseconds))

async function findOpenLoopbackPort() {
  return new Promise((resolve, reject) => {
    const server = createServer()
    server.unref()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const address = server.address()
      const port = typeof address === 'object' && address ? address.port : null
      server.close((error) => {
        if (error || !port) {
          reject(error || new Error('Unable to allocate a loopback port.'))
        } else {
          resolve(port)
        }
      })
    })
  })
}

class BackendSupervisor extends EventEmitter {
  constructor({ appRoot, dataDirectory, isPackaged, resourcesPath, fetchImpl = fetch }) {
    super()
    this.appRoot = appRoot
    this.dataDirectory = dataDirectory
    this.isPackaged = isPackaged
    this.resourcesPath = resourcesPath
    this.fetchImpl = fetchImpl
    this.child = null
    this.childFailure = null
    this.stopping = false
    this.port = null
    this.secret = null
  }

  async start() {
    if (this.child) {
      throw new Error('The backend is already running.')
    }

    this.port = await findOpenLoopbackPort()
    this.secret = randomBytes(32).toString('base64url')
    const launch = this.getLaunchCommand()
    const child = spawn(launch.command, launch.args, {
      cwd: launch.cwd,
      env: {
        ...process.env,
        STOCKSYNC_API_SECRET: this.secret,
        STOCKSYNC_BACKEND_PORT: String(this.port),
        STOCKSYNC_DATA_DIR: this.dataDirectory,
        DOTNET_ENVIRONMENT: this.isPackaged ? 'Production' : 'Development',
      },
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    })

    this.child = child
    child.stdout.on('data', (chunk) => process.stdout.write(`[backend] ${chunk}`))
    child.stderr.on('data', (chunk) => process.stderr.write(`[backend] ${chunk}`))
    child.once('error', (error) => {
      this.childFailure = error
    })
    child.once('exit', (code, signal) => {
      this.child = null
      const details = `Backend exited with code ${code ?? 'none'}${signal ? ` (${signal})` : ''}.`
      this.childFailure ||= new Error(details)
      if (!this.stopping) {
        this.emit('unexpected-exit', this.childFailure)
      }
    })

    await this.waitUntilReady()
  }

  getLaunchCommand() {
    if (this.isPackaged) {
      return {
        command: path.join(this.resourcesPath, 'backend', 'StockSyncAI.Api.exe'),
        args: [],
        cwd: path.join(this.resourcesPath, 'backend'),
      }
    }

    return {
      command: 'dotnet',
      args: [
        'run',
        '--project',
        path.join(this.appRoot, 'backend', 'StockSyncAI.Api', 'StockSyncAI.Api.csproj'),
        '--no-launch-profile',
        '--no-build',
      ],
      cwd: this.appRoot,
    }
  }

  async waitUntilReady(timeoutMilliseconds = 15000) {
    const deadline = Date.now() + timeoutMilliseconds
    let lastError = null

    while (Date.now() < deadline) {
      if (this.childFailure) {
        throw new Error(`Backend failed during startup: ${this.childFailure.message}`)
      }

      try {
        const response = await this.request('/health', { timeoutMilliseconds: 1000 })
        if (response.ok && response.data?.status === 'ready') {
          return response.data
        }
        lastError = new Error(`Health endpoint returned HTTP ${response.status}.`)
      } catch (error) {
        lastError = error
      }

      await delay(200)
    }

    throw new Error(
      `Backend readiness timed out after ${timeoutMilliseconds}ms${
        lastError ? `: ${lastError.message}` : '.'
      }`,
    )
  }

  async request(route, { method = 'GET', timeoutMilliseconds = 2000 } = {}) {
    if (!this.port || !this.secret) {
      throw new Error('The backend has not been started.')
    }

    const response = await this.fetchImpl(`http://127.0.0.1:${this.port}${route}`, {
      method,
      headers: { 'X-StockSync-Token': this.secret },
      signal: AbortSignal.timeout(timeoutMilliseconds),
    })
    const data = response.status === 202 ? null : await response.json()
    return { ok: response.ok, status: response.status, data }
  }

  async health() {
    if (this.childFailure || !this.child) {
      throw this.childFailure || new Error('The local backend is not running.')
    }

    const response = await this.request('/api/status')
    if (!response.ok) {
      throw new Error(`Backend health request failed with HTTP ${response.status}.`)
    }
    return response.data
  }

  async posStatus() {
    if (this.childFailure || !this.child) {
      throw this.childFailure || new Error('The local backend is not running.')
    }

    const response = await this.request('/api/pos/status')
    if (!response.ok) {
      throw new Error(`POS status request failed with HTTP ${response.status}.`)
    }
    return response.data
  }

  async stop() {
    if (!this.child) {
      return
    }

    this.stopping = true
    const child = this.child
    try {
      await this.request('/api/shutdown', { method: 'POST', timeoutMilliseconds: 1000 })
    } catch (error) {
      process.stderr.write(`[backend] Graceful shutdown request failed: ${error.message}\n`)
    }

    await Promise.race([
      new Promise((resolve) => child.once('exit', resolve)),
      delay(3000),
    ])

    if (this.child === child) {
      child.kill()
    }
  }
}

module.exports = { BackendSupervisor, findOpenLoopbackPort }
