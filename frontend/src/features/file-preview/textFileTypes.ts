import definitions from '../../../../shared/text-file-types.json';
export type TextLanguage = 'plain' | 'json' | 'xml' | 'markdown' | 'sql' | 'javascript' | 'typescript' | 'css' | 'html' | 'csharp' | 'python';
export type TextFileType = { extensions: string[]; language: TextLanguage; label: string; mime: string; aliases: string[]; renderedPreview?: boolean };
export const textFileTypes = definitions as TextFileType[];
export const plainTextType = textFileTypes.find(type => type.language === 'plain')!;
const byExtension = new Map(textFileTypes.flatMap(type => type.extensions.map(extension => [extension, type] as const)));
export function resolveTextFileType(file: { name: string; contentType?: string | null }): TextFileType | undefined {
  const type = byExtension.get(file.name.split('.').at(-1)?.toLowerCase() || '');
  const mime = file.contentType?.split(';')[0].trim().toLowerCase();
  return type && (!mime || mime === 'application/octet-stream' || mime === 'text/plain' || mime === type.mime || type.aliases.includes(mime)) ? type : undefined;
}
// Preserve existing MIME-only previews without making unregistered extensions editable.
export function previewTextFileType(file: { name: string; contentType?: string | null }): TextFileType {
  const mime = file.contentType?.split(';')[0].trim().toLowerCase();
  return resolveTextFileType(file) || textFileTypes.find(type => type.mime === mime || type.aliases.includes(mime || '')) || plainTextType;
}
