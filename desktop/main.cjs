const path = require('node:path')
const { createWriteStream } = require('node:fs')
const { mkdir, writeFile } = require('node:fs/promises')
const { pipeline } = require('node:stream/promises')
const { Readable } = require('node:stream')
const { randomUUID } = require('node:crypto')
const { app, BrowserWindow, dialog, ipcMain, protocol, shell } = require('electron')
const { BackendSupervisor } = require('./backend-supervisor.cjs')

protocol.registerSchemesAsPrivileged([
  {
    scheme: 'stocksync-invoice',
    privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true },
  },
])

let mainWindow = null
let backend = null
let shutdownStarted = false
const invoiceSelections = new Map()
const invoiceIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const emptyGeminiEnvironment = `GEMINI_API_KEY=
GEMINI_MODEL=gemini-2.5-flash
GEMINI_REQUEST_TIMEOUT_SECONDS=90
GEMINI_MAX_RETRIES=2
`

async function ensureEnvironmentFile(environmentFile) {
  await mkdir(path.dirname(environmentFile), { recursive: true })
  try {
    await writeFile(environmentFile, emptyGeminiEnvironment, {
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

function isTrustedSender(event) {
  return Boolean(mainWindow && event.sender === mainWindow.webContents)
}

function registerIpc() {
  ipcMain.handle('app:get-version', (event) => {
    if (!isTrustedSender(event)) {
      throw new Error('Untrusted IPC sender.')
    }
    return app.getVersion()
  })

  ipcMain.handle('backend:get-health', async (event) => {
    if (!isTrustedSender(event)) {
      return { ok: false, error: 'Untrusted IPC sender.' }
    }

    try {
      const data = await backend.health()
      return { ok: true, data: { ...data, applicationVersion: app.getVersion() } }
    } catch (error) {
      return {
        ok: false,
        error: error instanceof Error ? error.message : 'The local backend is unavailable.',
      }
    }
  })

  ipcMain.handle('pos:get-status', async (event) => {
    if (!isTrustedSender(event)) {
      return { ok: false, error: 'Untrusted IPC sender.' }
    }

    try {
      return { ok: true, data: await backend.posStatus() }
    } catch (error) {
      return {
        ok: false,
        error: error instanceof Error ? error.message : 'POS status is unavailable.',
      }
    }
  })

  ipcMain.handle('invoice:select', async (event) => {
    if (!isTrustedSender(event)) {
      return { ok: false, error: 'Untrusted IPC sender.' }
    }

    const result = await dialog.showOpenDialog(mainWindow, {
      title: 'Select supplier invoice',
      properties: ['openFile'],
      filters: [
        { name: 'Invoice documents', extensions: ['pdf', 'jpg', 'jpeg', 'png', 'webp'] },
      ],
    })
    if (result.canceled || result.filePaths.length !== 1) {
      return { ok: true, data: null }
    }

    try {
      const filePath = path.resolve(result.filePaths[0])
      const file = await require('node:fs/promises').stat(filePath)
      if (!file.isFile()) {
        throw new Error('The selected item is not a file.')
      }
      invoiceSelections.clear()
      const selectionToken = randomUUID()
      const idempotencyKey = randomUUID()
      const originalFileName = path.basename(filePath)
      invoiceSelections.set(selectionToken, {
        filePath,
        originalFileName,
        idempotencyKey,
      })
      return {
        ok: true,
        data: {
          selectionToken,
          originalFileName,
          fileSize: file.size,
          fileExtension: path.extname(originalFileName).toLowerCase(),
        },
      }
    } catch {
      return { ok: false, error: 'The selected invoice could not be read.' }
    }
  })

  ipcMain.handle('invoice:upload', async (event, payload) => {
    if (!isTrustedSender(event) || !payload || typeof payload.selectionToken !== 'string') {
      return { ok: false, error: 'Invalid invoice upload request.' }
    }
    const selection = invoiceSelections.get(payload.selectionToken)
    if (!selection) {
      return { ok: false, error: 'The invoice selection expired. Select the file again.' }
    }

    try {
      return { ok: true, data: await backend.uploadInvoice(selection) }
    } catch (error) {
      return { ok: false, error: safeError(error, 'The invoice could not be uploaded.') }
    }
  })

  ipcMain.handle('invoice:list', async (event, payload = {}) => {
    if (!isTrustedSender(event)) {
      return { ok: false, error: 'Untrusted IPC sender.' }
    }
    const page = Number.isInteger(payload.page) ? payload.page : 1
    const pageSize = Number.isInteger(payload.pageSize) ? payload.pageSize : 50
    if (page < 1 || pageSize < 1 || pageSize > 100) {
      return { ok: false, error: 'Invalid invoice list request.' }
    }
    try {
      return { ok: true, data: await backend.listInvoices(page, pageSize) }
    } catch (error) {
      return { ok: false, error: safeError(error, 'Unable to load invoices.') }
    }
  })

  ipcMain.handle('invoice:get', async (event, id) => {
    if (!isTrustedSender(event) || !isInvoiceId(id)) {
      return { ok: false, error: 'Invalid invoice request.' }
    }
    try {
      return { ok: true, data: await backend.getInvoice(id) }
    } catch (error) {
      return { ok: false, error: safeError(error, 'Unable to load the invoice.') }
    }
  })

  ipcMain.handle('invoice:delete', async (event, id) => {
    if (!isTrustedSender(event) || !isInvoiceId(id)) {
      return { ok: false, error: 'Invalid invoice deletion request.' }
    }
    try {
      await backend.deleteInvoice(id)
      return { ok: true }
    } catch (error) {
      return { ok: false, error: safeError(error, 'Unable to delete the invoice.') }
    }
  })

  ipcMain.handle('invoice:get-extraction', async (event, id) => {
    if (!isTrustedSender(event) || !isInvoiceId(id)) {
      return { ok: false, error: 'Invalid invoice extraction request.' }
    }
    try {
      return { ok: true, data: await backend.getInvoiceExtraction(id) }
    } catch (error) {
      return { ok: false, error: safeError(error, 'Unable to load invoice extraction data.') }
    }
  })

  ipcMain.handle('invoice:extract', async (event, id) => {
    if (!isTrustedSender(event) || !isInvoiceId(id)) {
      return { ok: false, error: 'Invalid invoice extraction request.' }
    }
    try {
      return { ok: true, data: await backend.extractInvoice(id) }
    } catch (error) {
      return { ok: false, error: safeError(error, 'Invoice extraction could not be completed.') }
    }
  })

  ipcMain.handle('invoice:save-copy', async (event, id) => {
    if (!isTrustedSender(event) || !isInvoiceId(id)) {
      return { ok: false, error: 'Invalid invoice download request.' }
    }
    try {
      const invoice = await backend.getInvoice(id)
      const destination = await dialog.showSaveDialog(mainWindow, {
        title: 'Save invoice copy',
        defaultPath: invoice.originalFileName,
      })
      if (destination.canceled || !destination.filePath) {
        return { ok: true, data: { saved: false } }
      }
      const response = await backend.invoiceFileResponse(id, true)
      if (!response.ok || !response.body) {
        throw new Error('The stored invoice file is unavailable.')
      }
      await pipeline(Readable.fromWeb(response.body), createWriteStream(destination.filePath))
      return { ok: true, data: { saved: true } }
    } catch (error) {
      return { ok: false, error: safeError(error, 'Unable to save the invoice copy.') }
    }
  })
}

function isInvoiceId(value) {
  return typeof value === 'string' && invoiceIdPattern.test(value)
}

function safeError(error, fallback) {
  return error instanceof Error && error.safeForRenderer && error.message
    ? error.message
    : fallback
}

function registerInvoiceProtocol() {
  protocol.handle('stocksync-invoice', async (request) => {
    if (request.method !== 'GET') {
      return new Response('Method not allowed.', { status: 405 })
    }
    const url = new URL(request.url)
    const id = url.pathname.replace(/^\//, '')
    if (url.hostname !== 'file' || !isInvoiceId(id)) {
      return new Response('Invalid invoice.', { status: 400 })
    }
    try {
      return await backend.invoiceFileResponse(id)
    } catch {
      return new Response('Invoice preview is unavailable.', { status: 503 })
    }
  })
}

async function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1120,
    height: 720,
    minWidth: 800,
    minHeight: 560,
    show: false,
    backgroundColor: '#07120f',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
    },
  })

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://')) {
      void shell.openExternal(url)
    }
    return { action: 'deny' }
  })
  mainWindow.webContents.on('will-navigate', (event) => event.preventDefault())
  mainWindow.once('ready-to-show', () => mainWindow?.show())

  const developmentUrl = process.env.STOCKSYNC_DEV_SERVER_URL
  if (developmentUrl) {
    await mainWindow.loadURL(developmentUrl)
  } else {
    await mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'))
  }
}

app.whenReady().then(async () => {
  try {
    const dataDirectory = path.join(app.getPath('userData'), 'data')
    const environmentFile = app.isPackaged
      ? path.join(app.getPath('userData'), '.env')
      : path.join(app.getAppPath(), '.env')
    await ensureEnvironmentFile(environmentFile)
    backend = new BackendSupervisor({
      appRoot: app.getAppPath(),
      dataDirectory,
      environmentFile,
      isPackaged: app.isPackaged,
      resourcesPath: process.resourcesPath,
    })
    backend.on('unexpected-exit', (error) => {
      dialog.showErrorBox(
        'StockSync AI backend stopped',
        `${error.message}\n\nRestart StockSync AI to restore local services.`,
      )
    })

    registerIpc()
    await backend.start()
    registerInvoiceProtocol()
    await createWindow()
  } catch (error) {
    dialog.showErrorBox(
      'StockSync AI could not start',
      error instanceof Error ? error.message : String(error),
    )
    await backend?.stop()
    app.quit()
  }
})

app.on('before-quit', (event) => {
  if (shutdownStarted || !backend) {
    return
  }

  event.preventDefault()
  shutdownStarted = true
  void backend.stop().finally(() => app.quit())
})

app.on('window-all-closed', () => app.quit())
