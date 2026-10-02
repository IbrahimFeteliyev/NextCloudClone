import { useEffect, useState } from 'react';
import { readText } from './content';
import type { PreviewTicket } from './content';
export function useTextContent(ticket: PreviewTicket, limit: number) {
  const [text, setText] = useState<string | null>(null); const [error, setError] = useState('');
  useEffect(() => {
    const abort = new AbortController(); setText(null); setError('');
    readText(ticket, abort.signal, limit).then(value => { if (!abort.signal.aborted) setText(value); }).catch(e => { if (!abort.signal.aborted) setError(e.message); });
    return () => abort.abort();
  }, [ticket, limit]);
  return { text, error, truncated: ticket.size > limit };
}
