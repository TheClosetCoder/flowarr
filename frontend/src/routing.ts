export const pageSlugs: Record<string, string> = {
  Dashboard: "dashboard",
  Connections: "connections",
  Storage: "storage",
  Downloads: "downloads",
  Transfers: "transfers",
  Cleanup: "cleanup",
  Settings: "settings"
};
const pageBySlug = Object.fromEntries(Object.entries(pageSlugs).map(([page, slug]) => [slug, page]));
export const connectionTabs = ["torrent", "sftp", "services", "plex", "jellyfin"];

export function getRouteFromHash() {
  const [pageSlug = "dashboard", tabSlug = "torrent"] = window.location.hash.replace(/^#\/?/, "").split("/");
  const page = pageBySlug[pageSlug.toLowerCase()] ?? "Dashboard";
  const connectionTab = connectionTabs.includes(tabSlug?.toLowerCase()) ? tabSlug.toLowerCase() : "torrent";
  return { page, connectionTab };
}

export function navigateToPage(page: string, currentConnectionTab: string) {
  const pageSlug = pageSlugs[page] ?? "dashboard";
  const tab = page === "Connections" ? `/${currentConnectionTab}` : "";
  window.location.hash = `${pageSlug}${tab}`;
}

export function navigateToConnectionTab(tab: string) {
  window.location.hash = `connections/${tab}`;
}
