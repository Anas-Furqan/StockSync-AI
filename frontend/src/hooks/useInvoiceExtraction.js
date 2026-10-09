import { useCallback, useEffect, useState } from 'react'
import {
  extractInvoice,
  getInvoiceExtraction,
} from '../services/invoices.js'

const initialState = {
  invoiceId: null,
  extraction: null,
  loading: true,
  extracting: false,
  error: null,
}

export default function useInvoiceExtraction(invoiceId) {
  const [state, setState] = useState(initialState)

  useEffect(() => {
    let active = true
    if (!invoiceId) {
      return () => {
        active = false
      }
    }

    getInvoiceExtraction(invoiceId)
      .then((extraction) => {
        if (active) {
          setState({ invoiceId, extraction, loading: false, extracting: false, error: null })
        }
      })
      .catch((error) => {
        if (active) {
          setState({
            invoiceId,
            extraction: null,
            loading: false,
            extracting: false,
            error: error instanceof Error
              ? error.message
              : 'Unable to load invoice extraction data.',
          })
        }
      })
    return () => {
      active = false
    }
  }, [invoiceId])

  const runExtraction = useCallback(async () => {
    if (!invoiceId || state.extracting) {
      return
    }
    setState((current) => ({ ...current, extracting: true, error: null }))
    try {
      const extraction = await extractInvoice(invoiceId)
      setState({ invoiceId, extraction, loading: false, extracting: false, error: null })
    } catch (error) {
      let extraction = state.extraction
      try {
        extraction = await getInvoiceExtraction(invoiceId)
      } catch {
        // Retain the last visible result if refreshing the failure state also fails.
      }
      setState({
        invoiceId,
        extraction,
        loading: false,
        extracting: false,
        error: error instanceof Error
          ? error.message
          : 'Invoice extraction could not be completed.',
      })
    }
  }, [invoiceId, state.extracting, state.extraction])

  if (invoiceId && state.invoiceId !== invoiceId) {
    return { ...initialState, loading: true, runExtraction }
  }
  return { ...state, runExtraction }
}
