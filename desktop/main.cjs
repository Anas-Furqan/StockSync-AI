const path = require('node:path')
const { app, BrowserWindow, dialog, ipcMain, shell } = require('electron')
const { BackendSupervisor } = require('./backend-supervisor.cjs')

let mainWindow = null
let backend = null
let shutdownStarted = false

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
    backend = new BackendSupervisor({
      appRoot: app.getAppPath(),
      dataDirectory: path.join(app.getPath('userData'), 'data'),
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
