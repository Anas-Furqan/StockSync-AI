import { useCallback, useEffect, useState } from 'react'
import {
  deleteInvoice,
  listInvoices,
  saveInvoiceCopy,
  selectInvoice,
  uploadInvoice,
} from '../services/invoices.js'

const initialState = {
  invoices: [],
  totalCount: 0,
  selectedFile: null,
  loading: true,
  uploading: false,
  deletingId: null,
  error: null,
  notice: null,
}

export default function useInvoices() {
  const [state, setState] = useState(initialState)

  const refresh = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: null }))
    try {
      const page = await listInvoices()
      setState((current) => ({
        ...current,
        invoices: page.items,
        totalCount: page.totalCount,
        loading: false,
      }))
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load invoices.',
      }))
    }
  }, [])

  useEffect(() => {
    let active = true
    listInvoices()
      .then((page) => {
        if (active) {
          setState((current) => ({
            ...current,
            invoices: page.items,
            totalCount: page.totalCount,
            loading: false,
          }))
        }
      })
      .catch((error) => {
        if (active) {
          setState((current) => ({
            ...current,
            loading: false,
            error: error instanceof Error ? error.message : 'Unable to load invoices.',
          }))
        }
      })
    return () => {
      active = false
    }
  }, [])

  const chooseFile = useCallback(async () => {
    setState((current) => ({ ...current, error: null, notice: null }))
    try {
      const selectedFile = await selectInvoice()
      if (selectedFile) {
        setState((current) => ({ ...current, selectedFile }))
      }
    } catch (error) {
      setState((current) => ({
        ...current,
        error: error instanceof Error ? error.message : 'Unable to select an invoice.',
      }))
    }
  }, [])

  const upload = useCallback(async () => {
    if (!state.selectedFile || state.uploading) {
      return
    }
    setState((current) => ({ ...current, uploading: true, error: null, notice: null }))
    try {
      const invoice = await uploadInvoice(state.selectedFile.selectionToken)
      const page = await listInvoices()
      setState((current) => ({
        ...current,
        invoices: page.items,
        totalCount: page.totalCount,
        selectedFile: null,
        uploading: false,
        notice: `${invoice.originalFileName} was uploaded.`,
      }))
    } catch (error) {
      setState((current) => ({
        ...current,
        uploading: false,
        error: error instanceof Error ? error.message : 'Unable to upload the invoice.',
      }))
    }
  }, [state.selectedFile, state.uploading])

  const remove = useCallback(async (invoice) => {
    setState((current) => ({
      ...current,
      deletingId: invoice.id,
      error: null,
      notice: null,
    }))
    try {
      await deleteInvoice(invoice.id)
      setState((current) => ({
        ...current,
        invoices: current.invoices.filter((item) => item.id !== invoice.id),
        totalCount: Math.max(0, current.totalCount - 1),
        deletingId: null,
        notice: `${invoice.originalFileName} was deleted.`,
      }))
    } catch (error) {
      setState((current) => ({
        ...current,
        deletingId: null,
        error: error instanceof Error ? error.message : 'Unable to delete the invoice.',
      }))
    }
  }, [])

  const saveCopy = useCallback(async (id) => {
    try {
      await saveInvoiceCopy(id)
    } catch (error) {
      setState((current) => ({
        ...current,
        error: error instanceof Error ? error.message : 'Unable to save the invoice copy.',
      }))
    }
  }, [])

  return { ...state, chooseFile, upload, refresh, remove, saveCopy }
}
