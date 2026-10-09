import { useCallback, useEffect, useState } from 'react'
import { getPosConnectionStatus } from '../services/backend.js'

export default function usePosConnectionStatus() {
  const [state, setState] = useState({
    status: null,
    error: null,
    loading: true,
  })

  const check = useCallback(async () => {
    setState((current) => ({ ...current, error: null, loading: true }))
    try {
      const status = await getPosConnectionStatus()
      setState({ status, error: null, loading: false })
    } catch (error) {
      setState({
        status: null,
        error: error instanceof Error ? error.message : 'Unable to check the POS connection.',
        loading: false,
      })
    }
  }, [])

  useEffect(() => {
    let active = true

    getPosConnectionStatus()
      .then((status) => {
        if (active) {
          setState({ status, error: null, loading: false })
        }
      })
      .catch((error) => {
        if (active) {
          setState({
            status: null,
            error:
              error instanceof Error
                ? error.message
                : 'Unable to check the POS connection.',
            loading: false,
          })
        }
      })

    return () => {
      active = false
    }
  }, [])

  return { ...state, check }
}
