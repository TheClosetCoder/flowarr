export const pageSlugs: Record<string, string> = {
  Dashboard: "dashboard",
  Storage: "storage",
  Downloads: "downloads",
  Transfers: "transfers",
  Cleanup: "cleanup",
  Settings: "settings"
};
const pageBySlug = Object.fromEntries(Object.entries(pageSlugs).map(([page, slug]) => [slug, page]));
export const connectionTabs = ["general", "torrent", "sftp", "services", "plex", "jellyfin"];

export function getRouteFromHash() {
  const [pageSlug = "dashboard", tabSlug = "general"] = window.location.hash.replace(/^#\/?/, "").split("/");
  const page = pageBySlug[pageSlug.toLowerCase()] ?? "Dashboard";
  const connectionTab = connectionTabs.includes(tabSlug?.toLowerCase()) ? tabSlug.toLowerCase() : "general";
  return { page, connectionTab };
}

export function navigateToPage(page: string, currentConnectionTab: string) {
  const pageSlug = pageSlugs[page] ?? "dashboard";
  const tab = page === "Settings" ? `/${currentConnectionTab}` : "";
  window.location.hash = `${pageSlug}${tab}`;
}

export function navigateToConnectionTab(tab: string) {
  window.location.hash = `settings/${tab}`;
}
