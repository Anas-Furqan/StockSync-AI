import { test } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { syncFrontend } from './sync-frontend.mjs'

test('syncFrontend rejects missing build output without changing destination', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'stocksync-sync-'))
  const destination = path.join(root, 'destination')
  await mkdir(destination)
  await writeFile(path.join(destination, 'keep.txt'), 'safe')

  await assert.rejects(
    syncFrontend({ source: path.join(root, 'missing'), destination }),
    /Frontend build output is missing/,
  )
  assert.equal(await readFile(path.join(destination, 'keep.txt'), 'utf8'), 'safe')
  await rm(root, { recursive: true, force: true })
})

test('syncFrontend atomically replaces its dedicated destination and is repeatable', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'stocksync-sync-'))
  const source = path.join(root, 'source')
  const destination = path.join(root, 'destination')
  await mkdir(source)
  await writeFile(path.join(source, 'index.html'), '<h1>first</h1>')

  await syncFrontend({ source, destination })
  await writeFile(path.join(source, 'index.html'), '<h1>second</h1>')
  await syncFrontend({ source, destination })

  assert.equal(
    await readFile(path.join(destination, 'index.html'), 'utf8'),
    '<h1>second</h1>',
  )
  await rm(root, { recursive: true, force: true })
})
