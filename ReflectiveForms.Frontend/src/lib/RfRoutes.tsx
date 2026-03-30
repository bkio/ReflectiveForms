import { Route } from 'react-router-dom';
import { DashboardPage } from '../pages/DashboardPage';
import { EntityEditPage } from '../pages/EntityEditPage';
import { EntityListPage } from '../pages/EntityListPage';
import { EntityViewPage } from '../pages/EntityViewPage';

export function RfRoutes() {
  return (
    <>
      <Route path="/" element={<DashboardPage />} />
      <Route path="/entities/:entityName" element={<EntityListPage />} />
      <Route path="/entities-admin/:entityName" element={<EntityEditPage />} />
      <Route path="/entities-view/:entityName" element={<EntityViewPage />} />
    </>
  );
}
