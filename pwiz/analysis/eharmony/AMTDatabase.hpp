///
/// AMTDatabase.hpp
///

#ifndef _AMTDATABASE_HPP_
#define _AMTDATABASE_HPP_

#include "AMTContainer.hpp"

namespace pwiz{
namespace eharmony{

using namespace pwiz::data::peakdata;

struct AMTDatabase
{
    AMTDatabase(const AMTContainer& amtContainer);
    std::vector<boost::shared_ptr<SpectrumQuery> > query(const Feature& f) ;
    std::vector<boost::shared_ptr<SpectrumQuery> > query(const double& mz, const double& rt) ;
    std::vector<SpectrumQuery> query(DataFetcherContainer& dfc, const WarpFunctionEnum& wfe, const SearchNeighborhoodCalculator& snc);
    std::vector<SpectrumQuery> query(DataFetcherContainer& dfc, const WarpFunctionEnum& wfe, SearchNeighborhoodCalculator& nds, MSMSPipelineAnalysis& mspa_in);
    
    PidfPtr _peptides;
    
};

} // namespace eharmony
} // namespace pwiz

#endif // _AMTDATABASE_HPP_
