import { Link } from 'react-router'

export function Placeholder({ title }: { title: string }) {
  return (
    <main className="page">
      <h1>{title}</h1>
      <p>開發中</p>
      <Link to="/">回首頁</Link>
    </main>
  )
}
