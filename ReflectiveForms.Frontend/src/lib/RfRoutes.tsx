import { Route } from 'react-router-dom';
import { DashboardPage } from '../pages/DashboardPage';
import { EntityEditPage } from '../pages/EntityEditPage';
import { EntityListPage } from '../pages/EntityListPage';
import { EntityViewPage } from '../pages/EntityViewPage';
import { RevisionDiffPage } from '../pages/RevisionDiffPage';
import { RfSheetListPage } from '../pages/RfSheetListPage';
import { RfSheetPage } from '../pages/RfSheetPage';
import { useGlobalSettings } from '../hooks/useEntity';

export function RfRoutes() {
  const settings = useGlobalSettings();
  const sheetsEnabled = settings.sheets_enabled !== false;

  return (
    <>
      <Route path="/" element={<DashboardPage />} />
      <Route path="/entities/:entityName" element={<EntityListPage />} />
      <Route path="/entities-admin/:entityName" element={<EntityEditPage />} />
      <Route path="/entities-view/:entityName" element={<EntityViewPage />} />
      <Route path="/entities-revisions/:entityName" element={<RevisionDiffPage />} />
      {sheetsEnabled && (
        <>
          <Route path="/sheets" element={<RfSheetListPage />} />
          <Route path="/sheets/:sheetId" element={<RfSheetPage />} />
        </>
      )}
    </>
  );
}
