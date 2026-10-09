export function jsonValidation(text: string): string {
  try { JSON.parse(text); return ''; } catch (error) { return (error as Error).message; }
}
export function formatJson(text: string, lineBreak = '\n'): { formatted?: string; error?: string } {
  try { return { formatted: JSON.stringify(JSON.parse(text), null, 2).replace(/\n/g, lineBreak) }; }
  catch (error) { return { error: (error as Error).message }; }
}
