// Where the project lives, for the header's GitHub link and each page's "Edit this page" link
export const repo = {
  url: 'https://github.com/feedfabric/feedfabric',
  branch: 'main',
  // This site's folder in the repository
  docsDir: 'web/docs',
}

// A page's source file from the repository root, from the absolute path Astro gives Markdown layouts
export function sourcePath(file: string | undefined): string | undefined {
  const at = file?.replaceAll('\\', '/').lastIndexOf(`/${repo.docsDir}/`) ?? -1
  return at >= 0 ? file!.replaceAll('\\', '/').slice(at + 1) : undefined
}

export const editUrl = (path: string) => `${repo.url}/edit/${repo.branch}/${path}`
