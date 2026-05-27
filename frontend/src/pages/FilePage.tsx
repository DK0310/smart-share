import { useParams } from 'react-router-dom';

export default function FilePage() {
  const { code } = useParams<{ code: string }>();

  return (
    <div className="page">
      <h1>File: {code}</h1>
      <p>File preview/download page. Coming in Phase 2.</p>
    </div>
  );
}
