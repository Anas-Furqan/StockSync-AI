import { useCallback, useEffect, useState } from 'react'
import { getBackendHealth } from '../services/backend.js'

export default function useBackendHealth() {
  const [state, setState] = useState({
    health: null,
    error: null,
    loading: true,
  })

  const refresh = useCallback(async () => {
    setState((current) => ({ ...current, error: null, loading: true }))

    try {
      const health = await getBackendHealth()
      if (health?.status !== 'ready') {
        throw new Error('The local backend is not ready.')
      }
      setState({ health, error: null, loading: false })
    } catch (error) {
      setState({
        health: null,
        error:
          error instanceof Error
            ? error.message
            : 'Unable to contact the local backend.',
        loading: false,
      })
    }
  }, [])

  useEffect(() => {
    let active = true

    getBackendHealth()
      .then((health) => {
        if (health?.status !== 'ready') {
          throw new Error('The local backend is not ready.')
        }
        if (active) {
          setState({ health, error: null, loading: false })
        }
      })
      .catch((error) => {
        if (active) {
          setState({
            health: null,
            error:
              error instanceof Error
                ? error.message
                : 'Unable to contact the local backend.',
            loading: false,
          })
        }
      })

    return () => {
      active = false
    }
  }, [])

  return { ...state, refresh }
}
