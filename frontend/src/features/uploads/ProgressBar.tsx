export default function ProgressBar({ name, percent, status }: { name: string; percent: number; status: string }) {
  return <div className={`upload-progress ${status.toLowerCase()}`} role="progressbar" aria-label={`Upload progress for ${name}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}><span style={{ width: `${percent}%` }} /></div>;
}
