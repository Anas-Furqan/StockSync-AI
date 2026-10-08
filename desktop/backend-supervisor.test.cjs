const { test } = require('node:test')
const assert = require('node:assert/strict')
const { findOpenLoopbackPort } = require('./backend-supervisor.cjs')

test('findOpenLoopbackPort returns a valid ephemeral port', async () => {
  const port = await findOpenLoopbackPort()
  assert.equal(Number.isInteger(port), true)
  assert.equal(port > 0 && port <= 65535, true)
})
